using DeepShot.Core.Annotations;

namespace DeepShot.Core.Editing;

/// <summary>
/// 添加标注：撤销时移除，重做时把**同一对象**插回原来的层级（Id/CreationOrder 不变）。
/// </summary>
public sealed class AddAnnotationCommand : IEditCommand
{
    private readonly AnnotationDocument _document;
    private readonly Annotation _annotation;

    /// <summary>重做时插回的位置；首次执行为当前文档末尾。</summary>
    private int _index;

    public AddAnnotationCommand(AnnotationDocument document, Annotation annotation)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(annotation);

        if (document.Contains(annotation))
        {
            throw new ArgumentException("标注已在文档中，不能再添加。", nameof(annotation));
        }

        _document = document;
        _annotation = annotation;
        _index = document.Count;
    }

    public string Description => $"添加{KindName(_annotation.Kind)}";

    public void Execute()
    {
        if (_document.Contains(_annotation))
        {
            return;
        }

        _document.Insert(_annotation, Math.Clamp(_index, 0, _document.Count));
    }

    public void Undo()
    {
        var index = _document.IndexOf(_annotation);
        if (index < 0)
        {
            return;
        }

        _index = index;
        _document.RemoveAt(index);
    }

    internal static string KindName(AnnotationKind kind) => kind switch
    {
        AnnotationKind.Rectangle => "矩形",
        AnnotationKind.Ellipse => "椭圆",
        AnnotationKind.Arrow => "箭头",
        AnnotationKind.Line => "直线",
        AnnotationKind.Freehand => "画笔",
        AnnotationKind.Text => "文字",
        AnnotationKind.Mosaic => "马赛克",
        _ => "标注",
    };
}

/// <summary>
/// 删除标注：撤销时把同一对象插回原来的层级，恢复原位置与顺序。
/// </summary>
public sealed class RemoveAnnotationCommand : IEditCommand
{
    private readonly AnnotationDocument _document;
    private readonly Annotation _annotation;
    private int _index;

    /// <exception cref="ArgumentException">标注不在文档中（无法删除）。</exception>
    public RemoveAnnotationCommand(AnnotationDocument document, Annotation annotation)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(annotation);

        _index = document.IndexOf(annotation);
        if (_index < 0)
        {
            throw new ArgumentException("标注不在当前文档中，无法删除。", nameof(annotation));
        }

        _document = document;
        _annotation = annotation;
    }

    public string Description => $"删除{AddAnnotationCommand.KindName(_annotation.Kind)}";

    public void Execute()
    {
        var index = _document.IndexOf(_annotation);
        if (index < 0)
        {
            return;
        }

        _index = index;
        _document.RemoveAt(index);
    }

    public void Undo()
    {
        if (_document.Contains(_annotation))
        {
            return;
        }

        _document.Insert(_annotation, Math.Clamp(_index, 0, _document.Count));
    }
}

/// <summary>
/// 平移标注：撤销时反向平移同样的偏移量。对象身份不变。
/// </summary>
public sealed class MoveAnnotationCommand : IEditCommand
{
    private readonly Annotation _annotation;
    private readonly int _deltaX;
    private readonly int _deltaY;

    public MoveAnnotationCommand(Annotation annotation, int deltaX, int deltaY)
    {
        ArgumentNullException.ThrowIfNull(annotation);
        _annotation = annotation;
        _deltaX = deltaX;
        _deltaY = deltaY;
    }

    public string Description => $"移动{AddAnnotationCommand.KindName(_annotation.Kind)}";

    public void Execute() => _annotation.Move(_deltaX, _deltaY);

    public void Undo() => _annotation.Move(-_deltaX, -_deltaY);
}

/// <summary>
/// 缩放标注（区域型：矩形/椭圆/马赛克）：记录调整前后的包围盒，撤销写回旧包围盒。
/// </summary>
/// <remarks>
/// 一次拖拽手柄只产生一条命令（T16）：UI 在拖拽过程中直接改标注做实时预览，
/// 抬起时先回滚到初始包围盒、再用本命令应用最终包围盒，保证 Execute/Undo 对称。
/// </remarks>
public sealed class ResizeAnnotationCommand : IEditCommand
{
    private readonly Annotation _annotation;
    private readonly Geometry.PixelRect _before;
    private readonly Geometry.PixelRect _after;

    /// <param name="annotation">要缩放的标注（必须支持 Resize）。</param>
    /// <param name="before">调整前的包围盒。</param>
    /// <param name="after">调整后的包围盒。</param>
    public ResizeAnnotationCommand(Annotation annotation, Geometry.PixelRect before, Geometry.PixelRect after)
    {
        ArgumentNullException.ThrowIfNull(annotation);
        _annotation = annotation;
        _before = before;
        _after = after;
    }

    public string Description => $"调整{AddAnnotationCommand.KindName(_annotation.Kind)}大小";

    public void Execute() => _annotation.Resize(_after);

    public void Undo() => _annotation.Resize(_before);
}

/// <summary>
/// 重新设置线段标注（箭头/直线）的两个端点：撤销时写回原端点。
/// </summary>
public sealed class SetSegmentEndpointsCommand : IEditCommand
{
    private readonly SegmentAnnotation _segment;
    private readonly Geometry.PixelPoint _beforeStart;
    private readonly Geometry.PixelPoint _beforeEnd;
    private readonly Geometry.PixelPoint _afterStart;
    private readonly Geometry.PixelPoint _afterEnd;

    public SetSegmentEndpointsCommand(
        SegmentAnnotation segment,
        Geometry.PixelPoint beforeStart,
        Geometry.PixelPoint beforeEnd,
        Geometry.PixelPoint afterStart,
        Geometry.PixelPoint afterEnd)
    {
        ArgumentNullException.ThrowIfNull(segment);
        _segment = segment;
        _beforeStart = beforeStart;
        _beforeEnd = beforeEnd;
        _afterStart = afterStart;
        _afterEnd = afterEnd;
    }

    public string Description => $"调整{AddAnnotationCommand.KindName(_segment.Kind)}端点";

    public void Execute() => _segment.SetEndpoints(_afterStart, _afterEnd);

    public void Undo() => _segment.SetEndpoints(_beforeStart, _beforeEnd);
}

/// <summary>
/// 清空文档：撤销时按原顺序把全部对象插回（恢复原 Z 序与 CreationOrder）。
/// </summary>
public sealed class ClearAnnotationsCommand : IEditCommand
{
    private readonly AnnotationDocument _document;
    private IReadOnlyList<Annotation> _removed = Array.Empty<Annotation>();

    public ClearAnnotationsCommand(AnnotationDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        _document = document;
    }

    public string Description => "清空标注";

    public void Execute() => _removed = _document.Clear();

    public void Undo()
    {
        for (var i = 0; i < _removed.Count; i++)
        {
            if (!_document.Contains(_removed[i]))
            {
                _document.Insert(_removed[i], i);
            }
        }
    }
}

/// <summary>
/// 修改标注属性：构造时读取旧值，执行时写入新值，撤销时写回旧值。
/// </summary>
/// <remarks>
/// 属性 setter 可能做范围钳制（粗细/字号/块大小）。旧值来自 setter 的读取结果，
/// 因此撤销写入的也是钳制后的合法值；重做重复写入新值同样会被钳制，结果一致。
/// </remarks>
public sealed class AnnotationPropertyCommand<T> : IEditCommand
{
    private readonly Annotation _target;
    private readonly string _propertyName;
    private readonly T _oldValue;
    private readonly T _newValue;
    private readonly Action<Annotation, T> _write;

    /// <param name="target">要修改的标注。</param>
    /// <param name="propertyName">属性名（用于说明/日志）。</param>
    /// <param name="newValue">新值。</param>
    /// <param name="read">读取属性的委托。</param>
    /// <param name="write">写入属性的委托。</param>
    public AnnotationPropertyCommand(
        Annotation target,
        string propertyName,
        T newValue,
        Func<Annotation, T> read,
        Action<Annotation, T> write)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(write);

        _target = target;
        _propertyName = propertyName;
        _newValue = newValue;
        _oldValue = read(target);
        _write = write;
    }

    public string Description => $"修改{_propertyName}";

    public void Execute() => _write(_target, _newValue);

    public void Undo() => _write(_target, _oldValue);
}

/// <summary>
/// 常用标注编辑命令的工厂，避免调用方自己拼属性读取/写入委托。
/// </summary>
public static class AnnotationEdits
{
    /// <summary>平移标注。</summary>
    public static IEditCommand Move(Annotation target, int deltaX, int deltaY) =>
        new MoveAnnotationCommand(target, deltaX, deltaY);

    /// <summary>缩放区域型标注到新的包围盒。</summary>
    public static IEditCommand Resize(Annotation target, Geometry.PixelRect before, Geometry.PixelRect after) =>
        new ResizeAnnotationCommand(target, before, after);

    /// <summary>设置线段标注的两个端点。</summary>
    public static IEditCommand SetEndpoints(
        SegmentAnnotation target,
        Geometry.PixelPoint beforeStart,
        Geometry.PixelPoint beforeEnd,
        Geometry.PixelPoint afterStart,
        Geometry.PixelPoint afterEnd) =>
        new SetSegmentEndpointsCommand(target, beforeStart, beforeEnd, afterStart, afterEnd);

    /// <summary>修改颜色（含透明度）。</summary>
    public static IEditCommand SetColor(Annotation target, AnnotationColor color) =>
        new AnnotationPropertyCommand<AnnotationColor>(
            target,
            "颜色",
            color,
            annotation => annotation.Color,
            (annotation, value) => annotation.Color = value);

    /// <summary>修改粗细（写入时按 1–20 钳制）。</summary>
    public static IEditCommand SetThickness(Annotation target, int thickness) =>
        new AnnotationPropertyCommand<int>(
            target,
            "粗细",
            thickness,
            annotation => annotation.Thickness,
            (annotation, value) => annotation.Thickness = value);

    /// <summary>修改文字内容。</summary>
    public static IEditCommand SetText(TextAnnotation target, string text) =>
        new AnnotationPropertyCommand<string>(
            target,
            "文字",
            text,
            annotation => ((TextAnnotation)annotation).Text,
            (annotation, value) => ((TextAnnotation)annotation).Text = value);

    /// <summary>修改字号（写入时按 8–72 钳制）。</summary>
    public static IEditCommand SetFontSize(TextAnnotation target, int fontSize) =>
        new AnnotationPropertyCommand<int>(
            target,
            "字号",
            fontSize,
            annotation => ((TextAnnotation)annotation).FontSize,
            (annotation, value) => ((TextAnnotation)annotation).FontSize = value);

    /// <summary>修改文字背景色（null 表示透明背景）。</summary>
    public static IEditCommand SetTextBackground(TextAnnotation target, AnnotationColor? background) =>
        new AnnotationPropertyCommand<AnnotationColor?>(
            target,
            "文字背景",
            background,
            annotation => ((TextAnnotation)annotation).BackgroundColor,
            (annotation, value) => ((TextAnnotation)annotation).BackgroundColor = value);

    /// <summary>修改马赛克块大小（写入时按 4–64 钳制）。</summary>
    public static IEditCommand SetMosaicBlockSize(MosaicAnnotation target, int blockSize) =>
        new AnnotationPropertyCommand<int>(
            target,
            "马赛克块大小",
            blockSize,
            annotation => ((MosaicAnnotation)annotation).BlockSize,
            (annotation, value) => ((MosaicAnnotation)annotation).BlockSize = value);

    /// <summary>修改马赛克强度（写入时按 0–1 钳制）。</summary>
    public static IEditCommand SetMosaicStrength(MosaicAnnotation target, double strength) =>
        new AnnotationPropertyCommand<double>(
            target,
            "马赛克强度",
            strength,
            annotation => ((MosaicAnnotation)annotation).Strength,
            (annotation, value) => ((MosaicAnnotation)annotation).Strength = value);

    /// <summary>修改画笔是否平滑。</summary>
    public static IEditCommand SetFreehandSmooth(FreehandAnnotation target, bool smooth) =>
        new AnnotationPropertyCommand<bool>(
            target,
            "画笔平滑",
            smooth,
            annotation => ((FreehandAnnotation)annotation).Smooth,
            (annotation, value) => ((FreehandAnnotation)annotation).Smooth = value);
}
