using ZeepCut.Core.Geometry;

namespace ZeepCut.Core.Annotations;

/// <summary>
/// 箭头标注：从 <see cref="SegmentAnnotation.Start"/> 指向
/// <see cref="SegmentAnnotation.End"/>，箭头画在终点。
/// </summary>
/// <remarks>
/// 模型只保存两端点，箭头头部的长度/角度由渲染层按粗细决定；零长度箭头合法
///（拖动距离为 0 时产生），包围盒为零尺寸，但仍可用容差命中。
/// </remarks>
public sealed class ArrowAnnotation : SegmentAnnotation
{
    public ArrowAnnotation(
        PixelPoint start,
        PixelPoint end,
        AnnotationColor? color = null,
        int thickness = AnnotationDefaults.DefaultThickness,
        Guid? id = null)
        : base(start, end, color, thickness, id)
    {
    }

    public override AnnotationKind Kind => AnnotationKind.Arrow;
}
