using ZeepCut.Core.Editing;
using ZeepCut.Core.Geometry;

namespace ZeepCut.Core.Annotations;

/// <summary>
/// 标注编辑状态机（T16）：选择已有标注、拖动移动、拖手柄缩放、拖线段端点、删除，
/// 所有改动都经过 <see cref="EditHistory"/> 记成**一条拖拽 = 一条历史**。
/// </summary>
/// <remarks>
/// <para>
/// 坐标与标注模型一致：**图像内物理像素**。UI 只负责 DIP↔像素换算、画选中态与手柄、
/// 以及把鼠标事件转发过来；命中、最小尺寸、夹取、撤销语义全部在这里，因此能单测。
/// </para>
/// <para>
/// 拖拽生命周期：<see cref="BeginDrag"/> 记录起点与初始几何 → 每次 <see cref="DragTo"/>
/// 直接改标注（实时预览，不写历史）→ <see cref="EndDrag"/> 比较初始与最终几何，
/// 有变化才先手动回滚到初始状态、再经 <see cref="EditHistory.Execute"/> 应用最终状态。
/// 这样命令的 Execute/Undo 永远对称，且一次拖拽只产生一条可撤销记录。
/// </para>
/// <para>
/// 支持范围：矩形/椭圆/马赛克支持八向缩放；箭头/直线支持整体平移与拖两端点；
/// 画笔/文字只支持整体平移（不提供缩放，避免点串/字形被强行拉伸）。
/// 所有几何都夹取在图像 <c>[0,W]×[0,H]</c> 内，缩放到 <see cref="MinimumSize"/> 就停住（不翻转）。
/// </para>
/// </remarks>
public sealed class AnnotationEditController
{
    /// <summary>缩放/拖动后区域的最小边长（像素）。</summary>
    public const int MinimumSize = 4;

    /// <summary>手柄命中半径的默认值（像素）。</summary>
    public const int DefaultHandleRadius = 10;

    /// <summary>手柄方块的默认边长（像素，仅用于绘制建议）。</summary>
    public const int DefaultHandleSize = 8;

    private readonly AnnotationDocument _document;
    private readonly EditHistory _history;

    private Annotation? _selected;
    private AnnotationHandle _handle;
    private AnnotationEditMode _mode;
    private PixelPoint _dragStart;
    private PixelRect _initialBounds;
    private PixelPoint _initialStart;
    private PixelPoint _initialEnd;
    private int _appliedDeltaX;
    private int _appliedDeltaY;

    /// <param name="document">标注文档（与 <see cref="AnnotationSession.Document"/> 同一个实例）。</param>
    /// <param name="history">撤销历史（与 <see cref="AnnotationSession.History"/> 同一个实例）。</param>
    /// <param name="imageBounds">截图图像范围，通常是 <c>(0,0,width,height)</c>。</param>
    public AnnotationEditController(AnnotationDocument document, EditHistory history, PixelRect imageBounds)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(history);

        if (imageBounds.IsEmpty)
        {
            throw new ArgumentException("图像范围不能为空。", nameof(imageBounds));
        }

        _document = document;
        _history = history;
        ImageBounds = imageBounds;
    }

    /// <summary>当前选中的标注；没有选中时为 <c>null</c>。</summary>
    public Annotation? Selected => _selected;

    /// <summary>是否有选中标注。</summary>
    public bool HasSelection => _selected is not null;

    /// <summary>当前正在进行的编辑方式。</summary>
    public AnnotationEditMode Mode => _mode;

    /// <summary>当前抓住的手柄（未拖拽时为 <see cref="AnnotationHandle.None"/>）。</summary>
    public AnnotationHandle ActiveHandle => _handle;

    /// <summary>是否正在拖拽（移动/缩放/拖端点）。</summary>
    public bool IsDragging => _mode != AnnotationEditMode.None;

    /// <summary>截图图像范围（物理像素）。</summary>
    public PixelRect ImageBounds { get; }

    /// <summary>选中某个标注（传 <c>null</c> 等于取消选中）。拖拽过程中不可更换选中。</summary>
    public void Select(Annotation? annotation)
    {
        if (_mode != AnnotationEditMode.None)
        {
            return;
        }

        _selected = annotation;
    }

    /// <summary>取消选中；正在拖拽时忽略（由 <see cref="EndDrag"/> 收尾）。</summary>
    public bool ClearSelection()
    {
        if (_mode != AnnotationEditMode.None || _selected is null)
        {
            return false;
        }

        _selected = null;
        return true;
    }

    /// <summary>
    /// 若当前选中标注已不在文档中（例如撤销了"添加"或删除了它），取消选中。
    /// 撤销/重做后应调用一次。
    /// </summary>
    public void ValidateSelection()
    {
        if (_mode != AnnotationEditMode.None)
        {
            return;
        }

        if (_selected is not null && !_document.Contains(_selected))
        {
            _selected = null;
        }
    }

    /// <summary>
    /// 选中态下手柄的命中测试：返回被命中的手柄，没有命中返回 <see cref="AnnotationHandle.None"/>。
    /// </summary>
    /// <param name="point">图像内物理像素。</param>
    /// <param name="handleRadius">手柄命中半径（像素），负值按 0 处理。</param>
    public AnnotationHandle HitTestHandle(PixelPoint point, int handleRadius = DefaultHandleRadius)
    {
        if (_selected is null)
        {
            return AnnotationHandle.None;
        }

        var radius = Math.Max(0, handleRadius);

        if (_selected is SegmentAnnotation segment)
        {
            if (IsNear(point, segment.Start, radius))
            {
                return AnnotationHandle.StartPoint;
            }

            if (IsNear(point, segment.End, radius))
            {
                return AnnotationHandle.EndPoint;
            }

            return AnnotationHandle.None;
        }

        if (!_selected.CanResize)
        {
            return AnnotationHandle.None;
        }

        var bounds = _selected.Bounds;
        var handles = new (AnnotationHandle Handle, int X, int Y)[]
        {
            (AnnotationHandle.TopLeft, bounds.Left, bounds.Top),
            (AnnotationHandle.Top, bounds.Left + (bounds.Width / 2), bounds.Top),
            (AnnotationHandle.TopRight, bounds.Right, bounds.Top),
            (AnnotationHandle.Right, bounds.Right, bounds.Top + (bounds.Height / 2)),
            (AnnotationHandle.BottomRight, bounds.Right, bounds.Bottom),
            (AnnotationHandle.Bottom, bounds.Left + (bounds.Width / 2), bounds.Bottom),
            (AnnotationHandle.BottomLeft, bounds.Left, bounds.Bottom),
            (AnnotationHandle.Left, bounds.Left, bounds.Top + (bounds.Height / 2)),
        };

        foreach (var (handle, x, y) in handles)
        {
            if (IsNear(point, new PixelPoint(x, y), radius))
            {
                return handle;
            }
        }

        return AnnotationHandle.None;
    }

    /// <summary>
    /// 选中态下手柄的绘制位置（图像物理像素，中心点 + 边长）。
    /// 区域型 8 个、线段型 2 个、画笔/文字没有手柄。
    /// </summary>
    public IReadOnlyList<(AnnotationHandle Handle, PixelRect Rect)> GetHandleRects(
        int handleSize = DefaultHandleSize)
    {
        if (_selected is null)
        {
            return Array.Empty<(AnnotationHandle, PixelRect)>();
        }

        var size = Math.Max(1, handleSize);
        var half = size / 2;

        (AnnotationHandle Handle, int X, int Y)[] centers;

        if (_selected is SegmentAnnotation segment)
        {
            centers = new[]
            {
                (AnnotationHandle.StartPoint, segment.Start.X, segment.Start.Y),
                (AnnotationHandle.EndPoint, segment.End.X, segment.End.Y),
            };
        }
        else if (_selected.CanResize)
        {
            var bounds = _selected.Bounds;
            centers = new[]
            {
                (AnnotationHandle.TopLeft, bounds.Left, bounds.Top),
                (AnnotationHandle.Top, bounds.Left + (bounds.Width / 2), bounds.Top),
                (AnnotationHandle.TopRight, bounds.Right, bounds.Top),
                (AnnotationHandle.Right, bounds.Right, bounds.Top + (bounds.Height / 2)),
                (AnnotationHandle.BottomRight, bounds.Right, bounds.Bottom),
                (AnnotationHandle.Bottom, bounds.Left + (bounds.Width / 2), bounds.Bottom),
                (AnnotationHandle.BottomLeft, bounds.Left, bounds.Bottom),
                (AnnotationHandle.Left, bounds.Left, bounds.Top + (bounds.Height / 2)),
            };
        }
        else
        {
            return Array.Empty<(AnnotationHandle, PixelRect)>();
        }

        var result = new (AnnotationHandle, PixelRect)[centers.Length];
        for (var i = 0; i < centers.Length; i++)
        {
            var (handle, x, y) = centers[i];
            result[i] = (handle, new PixelRect(x - half, y - half, size, size));
        }

        return result;
    }

    /// <summary>选中标注的包围盒（图像物理像素）；没有选中时为空矩形。</summary>
    public PixelRect SelectionBounds => _selected?.Bounds ?? PixelRect.Empty;

    /// <summary>
    /// 在图像物理像素处按下：先试选中标注的手柄，再试文档命中（命中即选中并准备整体平移）。
    /// </summary>
    /// <param name="point">按下点（图像物理像素）。</param>
    /// <param name="handleRadius">手柄命中半径。</param>
    /// <param name="hitTolerance">文档命中容差（像素），通常给几像素便于点中细线。</param>
    /// <returns>是否开始了一次拖拽（手柄或选中标注）。</returns>
    public bool BeginDrag(
        PixelPoint point,
        int handleRadius = DefaultHandleRadius,
        int hitTolerance = 3)
    {
        if (_mode != AnnotationEditMode.None)
        {
            return false;
        }

        if (_selected is not null)
        {
            var hitHandle = HitTestHandle(point, handleRadius);
            if (hitHandle != AnnotationHandle.None)
            {
                return BeginHandleDrag(point, hitHandle);
            }
        }

        var hit = _document.HitTest(point, Math.Max(0, hitTolerance));
        if (hit is null)
        {
            _selected = null;
            return false;
        }

        _selected = hit;
        return BeginHandleDrag(point, AnnotationHandle.Move);
    }

    /// <summary>拖到新的图像物理像素：实时修改标注，不写历史。</summary>
    /// <returns>是否真的更新了标注几何。</returns>
    public bool DragTo(PixelPoint point)
    {
        if (_selected is null || _mode == AnnotationEditMode.None)
        {
            return false;
        }

        switch (_mode)
        {
            case AnnotationEditMode.Move:
                MoveTo(point);
                return true;

            case AnnotationEditMode.Resize:
                ResizeTo(point);
                return true;

            case AnnotationEditMode.StartPoint:
            case AnnotationEditMode.EndPoint:
                MoveEndpointTo(point);
                return true;

            default:
                return false;
        }
    }

    /// <summary>
    /// 结束拖拽：几何有变化时记一条历史（一次拖拽一条），无变化时不产生记录。
    /// </summary>
    /// <returns>记录的说明（例如"移动矩形"）；没有变化时返回 <c>null</c>。</returns>
    public string? EndDrag()
    {
        if (_selected is null || _mode == AnnotationEditMode.None)
        {
            _mode = AnnotationEditMode.None;
            _handle = AnnotationHandle.None;
            return null;
        }

        var annotation = _selected;
        var mode = _mode;

        _mode = AnnotationEditMode.None;
        _handle = AnnotationHandle.None;

        IEditCommand? command = null;

        switch (mode)
        {
            case AnnotationEditMode.Move:
            {
                var deltaX = _appliedDeltaX;
                var deltaY = _appliedDeltaY;
                if (deltaX != 0 || deltaY != 0)
                {
                    // 先回滚到初始位置，再让命令把最终位置应用一次，保证 Undo/Redo 对称。
                    annotation.Move(-deltaX, -deltaY);
                    command = new MoveAnnotationCommand(annotation, deltaX, deltaY);
                }

                break;
            }

            case AnnotationEditMode.Resize:
            {
                var current = annotation.Bounds;
                if (current != _initialBounds)
                {
                    annotation.Resize(_initialBounds);
                    command = new ResizeAnnotationCommand(annotation, _initialBounds, current);
                }

                break;
            }

            case AnnotationEditMode.StartPoint:
            case AnnotationEditMode.EndPoint:
            {
                if (annotation is SegmentAnnotation segment)
                {
                    var finalStart = segment.Start;
                    var finalEnd = segment.End;
                    if (finalStart != _initialStart || finalEnd != _initialEnd)
                    {
                        segment.SetEndpoints(_initialStart, _initialEnd);
                        command = new SetSegmentEndpointsCommand(
                            segment,
                            _initialStart,
                            _initialEnd,
                            finalStart,
                            finalEnd);
                    }
                }

                break;
            }
        }

        _appliedDeltaX = 0;
        _appliedDeltaY = 0;

        if (command is null)
        {
            return null;
        }

        _history.Execute(command);
        return command.Description;
    }

    /// <summary>取消当前拖拽：把标注恢复成拖拽前的几何，不写历史。</summary>
    /// <returns>是否真的取消了一次拖拽。</returns>
    public bool CancelDrag()
    {
        if (_selected is null || _mode == AnnotationEditMode.None)
        {
            _mode = AnnotationEditMode.None;
            _handle = AnnotationHandle.None;
            return false;
        }

        switch (_mode)
        {
            case AnnotationEditMode.Move:
                _selected.Move(-_appliedDeltaX, -_appliedDeltaY);
                break;

            case AnnotationEditMode.Resize:
                _selected.Resize(_initialBounds);
                break;

            case AnnotationEditMode.StartPoint:
            case AnnotationEditMode.EndPoint:
                if (_selected is SegmentAnnotation segment)
                {
                    segment.SetEndpoints(_initialStart, _initialEnd);
                }

                break;
        }

        _mode = AnnotationEditMode.None;
        _handle = AnnotationHandle.None;
        _appliedDeltaX = 0;
        _appliedDeltaY = 0;
        return true;
    }

    /// <summary>删除选中标注（一条可撤销记录），删除后取消选中。</summary>
    /// <returns>删除命令的说明；没有选中时返回 <c>null</c>。</returns>
    public string? DeleteSelected()
    {
        if (_selected is null || _mode != AnnotationEditMode.None || !_document.Contains(_selected))
        {
            return null;
        }

        var annotation = _selected;
        _selected = null;

        var command = new RemoveAnnotationCommand(_document, annotation);
        _history.Execute(command);
        return command.Description;
    }

    /// <summary>
    /// 修改选中标注的文字（双击编辑用），经 <see cref="AnnotationEdits.SetText"/> 记录；
    /// 未选中、不是文字标注或内容没变时返回 <c>null</c>。
    /// </summary>
    public string? SetSelectedText(string? text)
    {
        if (_selected is not TextAnnotation annotation || _mode != AnnotationEditMode.None)
        {
            return null;
        }

        var value = text ?? string.Empty;
        if (string.Equals(annotation.Text, value, StringComparison.Ordinal))
        {
            return null;
        }

        var command = AnnotationEdits.SetText(annotation, value);
        _history.Execute(command);
        return command.Description;
    }

    private bool BeginHandleDrag(PixelPoint point, AnnotationHandle handle)
    {
        _dragStart = point;
        _initialBounds = _selected!.Bounds;
        _appliedDeltaX = 0;
        _appliedDeltaY = 0;

        if (_selected is SegmentAnnotation segment)
        {
            _initialStart = segment.Start;
            _initialEnd = segment.End;
        }
        else
        {
            _initialStart = default;
            _initialEnd = default;
        }

        _handle = handle;
        _mode = handle switch
        {
            AnnotationHandle.Move => AnnotationEditMode.Move,
            AnnotationHandle.StartPoint => AnnotationEditMode.StartPoint,
            AnnotationHandle.EndPoint => AnnotationEditMode.EndPoint,
            _ => AnnotationEditMode.Resize,
        };

        return true;
    }

    private void MoveTo(PixelPoint point)
    {
        var annotation = _selected!;
        var bounds = _initialBounds;

        var desiredX = point.X - _dragStart.X;
        var desiredY = point.Y - _dragStart.Y;

        // 夹取：整体保持在图像内（尺寸大于图像时贴左上）。
        var maxX = Math.Max(0, ImageBounds.Width - bounds.Width);
        var maxY = Math.Max(0, ImageBounds.Height - bounds.Height);
        var targetLeft = Math.Clamp(bounds.Left + desiredX, ImageBounds.Left, ImageBounds.Left + maxX);
        var targetTop = Math.Clamp(bounds.Top + desiredY, ImageBounds.Top, ImageBounds.Top + maxY);

        var clampedX = targetLeft - bounds.Left;
        var clampedY = targetTop - bounds.Top;

        var stepX = clampedX - _appliedDeltaX;
        var stepY = clampedY - _appliedDeltaY;
        if (stepX == 0 && stepY == 0)
        {
            return;
        }

        annotation.Move(stepX, stepY);
        _appliedDeltaX = clampedX;
        _appliedDeltaY = clampedY;
    }

    private void ResizeTo(PixelPoint point)
    {
        var annotation = _selected!;
        var initial = _initialBounds;

        var left = initial.Left;
        var top = initial.Top;
        var right = initial.Right;
        var bottom = initial.Bottom;

        switch (_handle)
        {
            case AnnotationHandle.TopLeft:
                left = point.X;
                top = point.Y;
                break;
            case AnnotationHandle.Top:
                top = point.Y;
                break;
            case AnnotationHandle.TopRight:
                right = point.X;
                top = point.Y;
                break;
            case AnnotationHandle.Right:
                right = point.X;
                break;
            case AnnotationHandle.BottomRight:
                right = point.X;
                bottom = point.Y;
                break;
            case AnnotationHandle.Bottom:
                bottom = point.Y;
                break;
            case AnnotationHandle.BottomLeft:
                left = point.X;
                bottom = point.Y;
                break;
            case AnnotationHandle.Left:
                left = point.X;
                break;
            default:
                return;
        }

        // 先夹取到图像内，再保证最小尺寸（不翻转：拖过对面边缘就停在最小尺寸）。
        left = Math.Clamp(left, ImageBounds.Left, ImageBounds.Right);
        top = Math.Clamp(top, ImageBounds.Top, ImageBounds.Bottom);
        right = Math.Clamp(right, ImageBounds.Left, ImageBounds.Right);
        bottom = Math.Clamp(bottom, ImageBounds.Top, ImageBounds.Bottom);

        if (right - left < MinimumSize)
        {
            if (_handle is AnnotationHandle.Left or AnnotationHandle.TopLeft or AnnotationHandle.BottomLeft)
            {
                left = Math.Min(right - MinimumSize, right);
                if (left < ImageBounds.Left)
                {
                    left = ImageBounds.Left;
                    right = Math.Min(ImageBounds.Right, left + MinimumSize);
                }
            }
            else
            {
                right = Math.Max(left + MinimumSize, left);
                if (right > ImageBounds.Right)
                {
                    right = ImageBounds.Right;
                    left = Math.Max(ImageBounds.Left, right - MinimumSize);
                }
            }
        }

        if (bottom - top < MinimumSize)
        {
            if (_handle is AnnotationHandle.Top or AnnotationHandle.TopLeft or AnnotationHandle.TopRight)
            {
                top = Math.Min(bottom - MinimumSize, bottom);
                if (top < ImageBounds.Top)
                {
                    top = ImageBounds.Top;
                    bottom = Math.Min(ImageBounds.Bottom, top + MinimumSize);
                }
            }
            else
            {
                bottom = Math.Max(top + MinimumSize, top);
                if (bottom > ImageBounds.Bottom)
                {
                    bottom = ImageBounds.Bottom;
                    top = Math.Max(ImageBounds.Top, bottom - MinimumSize);
                }
            }
        }

        annotation.Resize(new PixelRect(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top)));
    }

    private void MoveEndpointTo(PixelPoint point)
    {
        if (_selected is not SegmentAnnotation segment)
        {
            return;
        }

        var x = Math.Clamp(point.X, ImageBounds.Left, ImageBounds.Right);
        var y = Math.Clamp(point.Y, ImageBounds.Top, ImageBounds.Bottom);
        var clamped = new PixelPoint(x, y);

        if (_mode == AnnotationEditMode.StartPoint)
        {
            segment.SetEndpoints(clamped, segment.End);
        }
        else
        {
            segment.SetEndpoints(segment.Start, clamped);
        }
    }

    private static bool IsNear(PixelPoint point, PixelPoint center, int radius) =>
        Math.Abs(point.X - center.X) <= radius && Math.Abs(point.Y - center.Y) <= radius;
}
