using DeepShot.Core.Geometry;

namespace DeepShot.Core.Annotations;

/// <summary>
/// 一次拖拽中的标注草稿：鼠标按下时创建具体标注对象，拖动时更新它，抬起时决定是否提交。
/// </summary>
/// <remarks>
/// <para>
/// 草稿对象本身就是最终标注对象（不是副本），拖动过程中直接渲染它就能得到实时预览；
/// 抬起时交给 <c>AddAnnotationCommand</c> 记入撤销历史（首次执行即加入到文档）。
/// </para>
/// <para>
/// 坐标全部是图像内物理像素；调用方负责把指针夹取到选区内（见
/// <see cref="AnnotationCanvasTransform.ToImageClamped"/>）。
/// </para>
/// <para>
/// “有意义”的判定（<see cref="IsMeaningful"/>）：区域型要求宽高都大于 0（单击不产生零面积矩形），
/// 线段型要求两个端点不同（单击不产生零长度线），画笔只要有至少一个点（单击画一个点）。
/// </para>
/// </remarks>
public sealed class AnnotationDraft
{
    private readonly PixelPoint _start;
    private PixelPoint _end;

    internal AnnotationDraft(AnnotationTool tool, Annotation annotation, PixelPoint start)
    {
        Tool = tool;
        Annotation = annotation;
        _start = start;
        _end = start;
    }

    /// <summary>拖拽使用的工具。</summary>
    public AnnotationTool Tool { get; }

    /// <summary>草稿对应的标注对象（拖动时被原地修改）。</summary>
    public Annotation Annotation { get; }

    /// <summary>按下点（图像物理像素）。</summary>
    public PixelPoint Start => _start;

    /// <summary>最近一次拖动到的点（图像物理像素）。</summary>
    public PixelPoint End => _end;

    /// <summary>画笔草稿当前的点数；其它工具为 0。</summary>
    public int PointCount => Annotation is FreehandAnnotation freehand ? freehand.Points.Count : 0;

    /// <summary>草稿是否值得提交（单击/零长度等退化操作返回 false）。</summary>
    public bool IsMeaningful => Tool switch
    {
        AnnotationTool.Rectangle or AnnotationTool.Ellipse or AnnotationTool.Mosaic => !Annotation.Bounds.IsEmpty,
        AnnotationTool.Line or AnnotationTool.Arrow => _start != _end,
        AnnotationTool.Freehand => Annotation is FreehandAnnotation { Points.Count: > 0 },
        _ => false,
    };

    /// <summary>拖动到新的点：按工具更新区域、端点或追加画笔点。</summary>
    public void DragTo(PixelPoint point)
    {
        _end = point;

        switch (Tool)
        {
            case AnnotationTool.Rectangle:
            case AnnotationTool.Ellipse:
            case AnnotationTool.Mosaic:
                Annotation.Resize(PixelRect.FromTwoPoints(_start.X, _start.Y, point.X, point.Y));
                break;

            case AnnotationTool.Line:
            case AnnotationTool.Arrow:
                ((SegmentAnnotation)Annotation).SetEndpoints(_start, point);
                break;

            case AnnotationTool.Freehand:
                var freehand = (FreehandAnnotation)Annotation;
                // 鼠标在同一像素上重复触发 MouseMove 时不追加重复点。
                if (freehand.Points.Count == 0 || freehand.Points[^1] != point)
                {
                    freehand.AddPoint(point);
                }

                break;

            case AnnotationTool.Text:
                break;
        }
    }
}

/// <summary>
/// 标注草稿工厂：按工具创建起始于某个图像物理像素的标注对象。
/// 文字工具不使用拖拽草稿，返回 <c>null</c>（由 UI 打开输入框，提交时直接创建
/// <see cref="TextAnnotation"/>）。
/// </summary>
public static class AnnotationDraftFactory
{
    /// <summary>
    /// 开始一次拖拽草稿；文字工具返回 <c>null</c>。
    /// </summary>
    /// <param name="tool">当前工具。</param>
    /// <param name="point">按下点（图像物理像素）。</param>
    /// <param name="color">颜色。</param>
    /// <param name="thickness">粗细（默认采用标注默认值）。</param>
    public static AnnotationDraft? Begin(
        AnnotationTool tool,
        PixelPoint point,
        AnnotationColor color,
        int thickness = AnnotationDefaults.DefaultThickness)
    {
        Annotation annotation;

        switch (tool)
        {
            case AnnotationTool.Rectangle:
                annotation = new RectangleAnnotation(point, point, color, thickness);
                break;

            case AnnotationTool.Ellipse:
                annotation = new EllipseAnnotation(point, point, color, thickness);
                break;

            case AnnotationTool.Arrow:
                annotation = new ArrowAnnotation(point, point, color, thickness);
                break;

            case AnnotationTool.Line:
                annotation = new LineAnnotation(point, point, color, thickness);
                break;

            case AnnotationTool.Freehand:
                // 画笔默认开启平滑（渲染层用轻量二次贝塞尔平滑点串）。
                annotation = new FreehandAnnotation(new[] { point }, smooth: true, color, thickness);
                break;

            case AnnotationTool.Mosaic:
                // 马赛克不使用颜色/粗细，块大小与强度取 AnnotationDefaults。
                annotation = new MosaicAnnotation(new PixelRect(point.X, point.Y, 0, 0));
                break;

            case AnnotationTool.Text:
                return null;

            default:
                throw new ArgumentOutOfRangeException(nameof(tool), tool, "未知标注工具。");
        }

        return new AnnotationDraft(tool, annotation, point);
    }
}
