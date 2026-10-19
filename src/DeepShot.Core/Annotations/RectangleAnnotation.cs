using DeepShot.Core.Geometry;

namespace DeepShot.Core.Annotations;

/// <summary>
/// 矩形框标注（非填充，只画边框）。用两个对角点或一个矩形描述区域。
/// </summary>
/// <remarks>
/// 任意拖拽方向都得到正矩形（宽高取绝对值）；<see cref="Region"/> 支持
/// <see cref="Resize"/> 与 <see cref="Move"/>。命中测试按区域内部 + 容差，
/// 这样选中标注时不必精确点中 1 像素的边框。
/// </remarks>
public sealed class RectangleAnnotation : Annotation
{
    /// <param name="region">矩形区域（负宽/负高会被规范化为正矩形）。</param>
    public RectangleAnnotation(
        PixelRect region,
        AnnotationColor? color = null,
        int thickness = AnnotationDefaults.DefaultThickness,
        Guid? id = null)
        : base(color, thickness, id)
    {
        Region = AnnotationGeometry.Normalize(region);
    }

    /// <param name="start">拖拽起点。</param>
    /// <param name="end">拖拽终点。</param>
    public RectangleAnnotation(
        PixelPoint start,
        PixelPoint end,
        AnnotationColor? color = null,
        int thickness = AnnotationDefaults.DefaultThickness,
        Guid? id = null)
        : this(PixelRect.FromTwoPoints(start.X, start.Y, end.X, end.Y), color, thickness, id)
    {
    }

    /// <summary>矩形区域（物理像素，宽高非负）。</summary>
    public PixelRect Region { get; private set; }

    public override AnnotationKind Kind => AnnotationKind.Rectangle;

    public override PixelRect Bounds => Region;

    public override bool CanResize => true;

    public override void Move(int dx, int dy) => Region = Region.Offset(dx, dy);

    public override void Resize(PixelRect bounds) => Region = AnnotationGeometry.Normalize(bounds);

    public override bool HitTest(PixelPoint point, int tolerance) =>
        AnnotationGeometry.ContainsExpanded(Region, point, tolerance);
}
