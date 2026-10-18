using ZeepCut.Core.Geometry;

namespace ZeepCut.Core.Annotations;

/// <summary>
/// 椭圆（或圆）标注（非填充，只画轮廓）。用包围盒描述。
/// </summary>
/// <remarks>
/// 命中测试：椭圆内部一律命中（便于选中），外部按"到椭圆边界的近似距离"与
/// 容差 + 描边半宽比较。包围盒四角在椭圆外，所以不会被误命中。
/// </remarks>
public sealed class EllipseAnnotation : Annotation
{
    /// <param name="region">椭圆包围盒（负宽/负高会被规范化为正矩形）。</param>
    public EllipseAnnotation(
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
    public EllipseAnnotation(
        PixelPoint start,
        PixelPoint end,
        AnnotationColor? color = null,
        int thickness = AnnotationDefaults.DefaultThickness,
        Guid? id = null)
        : this(PixelRect.FromTwoPoints(start.X, start.Y, end.X, end.Y), color, thickness, id)
    {
    }

    /// <summary>椭圆包围盒（物理像素，宽高非负）。</summary>
    public PixelRect Region { get; private set; }

    public override AnnotationKind Kind => AnnotationKind.Ellipse;

    public override PixelRect Bounds => Region;

    public override bool CanResize => true;

    public override void Move(int dx, int dy) => Region = Region.Offset(dx, dy);

    public override void Resize(PixelRect bounds) => Region = AnnotationGeometry.Normalize(bounds);

    public override bool HitTest(PixelPoint point, int tolerance)
    {
        var region = Region;
        if (region.Width <= 0 || region.Height <= 0)
        {
            // 退化椭圆（一条线或一个点）：按包围盒加容差判断，至少还能选中。
            return AnnotationGeometry.ContainsExpanded(region, point, tolerance);
        }

        var radiusX = region.Width / 2d;
        var radiusY = region.Height / 2d;
        var centerX = region.Left + radiusX;
        var centerY = region.Top + radiusY;

        var normalizedX = (point.X - centerX) / radiusX;
        var normalizedY = (point.Y - centerY) / radiusY;
        var normalized = normalizedX * normalizedX + normalizedY * normalizedY;

        if (normalized <= 1d)
        {
            return true;
        }

        // 近似：把归一化半径差换算成"离最近边界的像素距离"，短轴越小时越保守。
        var edgeDistance = (Math.Sqrt(normalized) - 1d) * Math.Min(radiusX, radiusY);
        return edgeDistance <= Math.Max(0, tolerance) + Thickness / 2d;
    }
}
