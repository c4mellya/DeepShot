using ZeepCut.Core.Geometry;

namespace ZeepCut.Core.Annotations;

/// <summary>
/// 马赛克标注：一个需要打码的区域 + 块大小 + 强度。
/// </summary>
/// <remarks>
/// <para>
/// 模型只保存区域与参数，实际像素处理由渲染层完成。块大小越界钳制到 4–64；
/// 强度是 0–1 的比例（0 表示不处理，1 表示最强），用于渲染层混合原图与马赛克图。
/// </para>
/// <para>
/// 命中测试按区域内部 + 容差；马赛克区域是"填充的"，点内部即可命中。
/// </para>
/// </remarks>
public sealed class MosaicAnnotation : Annotation
{
    private int _blockSize;
    private double _strength;

    /// <param name="region">马赛克区域（负宽/负高会被规范化为正矩形）。</param>
    /// <param name="blockSize">块大小（像素），越界会被钳制到 4–64。</param>
    /// <param name="strength">强度 0–1，越界会被钳制，NaN 回退到默认值。</param>
    public MosaicAnnotation(
        PixelRect region,
        int blockSize = AnnotationDefaults.DefaultMosaicBlockSize,
        double strength = AnnotationDefaults.DefaultMosaicStrength,
        AnnotationColor? color = null,
        int thickness = AnnotationDefaults.DefaultThickness,
        Guid? id = null)
        : base(color, thickness, id)
    {
        Region = AnnotationGeometry.Normalize(region);
        BlockSize = blockSize;
        Strength = strength;
    }

    /// <summary>马赛克区域（物理像素，宽高非负）。</summary>
    public PixelRect Region { get; private set; }

    /// <summary>块大小（像素），赋值时钳制到 4–64。</summary>
    public int BlockSize
    {
        get => _blockSize;
        set => _blockSize = AnnotationDefaults.ClampMosaicBlockSize(value);
    }

    /// <summary>强度（0–1），赋值时钳制；NaN 回退到默认值。</summary>
    public double Strength
    {
        get => _strength;
        set => _strength = AnnotationDefaults.ClampMosaicStrength(value);
    }

    public override AnnotationKind Kind => AnnotationKind.Mosaic;

    public override PixelRect Bounds => Region;

    public override bool CanResize => true;

    public override PixelRect GetRenderBounds() => Region;

    public override void Move(int dx, int dy) => Region = Region.Offset(dx, dy);

    public override void Resize(PixelRect bounds) => Region = AnnotationGeometry.Normalize(bounds);

    public override bool HitTest(PixelPoint point, int tolerance) =>
        AnnotationGeometry.ContainsExpanded(Region, point, tolerance);
}
