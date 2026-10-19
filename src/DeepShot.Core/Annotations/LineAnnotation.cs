using DeepShot.Core.Geometry;

namespace DeepShot.Core.Annotations;

/// <summary>直线标注：两端点之间的一条线段。</summary>
public sealed class LineAnnotation : SegmentAnnotation
{
    public LineAnnotation(
        PixelPoint start,
        PixelPoint end,
        AnnotationColor? color = null,
        int thickness = AnnotationDefaults.DefaultThickness,
        Guid? id = null)
        : base(start, end, color, thickness, id)
    {
    }

    public override AnnotationKind Kind => AnnotationKind.Line;
}
