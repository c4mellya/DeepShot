using ZeepCut.Core.Geometry;

namespace ZeepCut.Core.Annotations;

/// <summary>
/// 标注的抽象基类：所有标注共有的 Id、类型、颜色、粗细、创建顺序，
/// 以及包围盒、命中测试、移动/调整大小的统一契约。
/// </summary>
/// <remarks>
/// <para>
/// 坐标一律是**相对截图图像左上角的物理像素**（不是屏幕绝对坐标，也不是 WPF DIP）。
/// 这样导出时可以直接按图像坐标渲染，标注也不需要知道目标屏幕原点。
/// </para>
/// <para>
/// <see cref="Bounds"/> 是几何包围盒（不包含描边宽度），用于命中测试和渲染裁剪；
/// <see cref="GetRenderBounds"/> 在此基础上按粗细向外扩，供渲染器做保守裁剪。
/// 退化图形（单点画笔、零长度箭头、零宽矩形）的 <see cref="Bounds"/> 是零尺寸矩形，
/// 命中测试依赖容差（和描边半宽）而不是 <see cref="PixelRect.IsEmpty"/>。
/// </para>
/// <para>
/// <see cref="CreationOrder"/> 由 <see cref="AnnotationDocument"/> 在首次加入时分配，
/// 从 1 开始单调递增；撤销删除后重新加回同一对象时保留原值。渲染的 Z 序看文档中的
/// 位置（越靠后越在上层），不是创建顺序。
/// </para>
/// </remarks>
public abstract class Annotation
{
    private int _thickness;

    /// <param name="color">颜色；为空时用 <see cref="AnnotationColor.Default"/>。</param>
    /// <param name="thickness">粗细（像素），越界会被钳制到 1–20。</param>
    /// <param name="id">显式 Id（反序列化用）；为空时自动生成。不允许 <see cref="Guid.Empty"/>。</param>
    protected Annotation(
        AnnotationColor? color = null,
        int thickness = AnnotationDefaults.DefaultThickness,
        Guid? id = null)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("标注 Id 不能为空 Guid。", nameof(id));
        }

        Id = id ?? Guid.NewGuid();
        Color = color ?? AnnotationColor.Default;
        Thickness = thickness;
    }

    /// <summary>稳定标识（反序列化/撤销重做时标识同一对象）。</summary>
    public Guid Id { get; }

    /// <summary>具体类型。</summary>
    public abstract AnnotationKind Kind { get; }

    /// <summary>颜色（ARGB）。</summary>
    public AnnotationColor Color { get; set; }

    /// <summary>粗细（像素），赋值时钳制到 1–20。</summary>
    public int Thickness
    {
        get => _thickness;
        set => _thickness = AnnotationDefaults.ClampThickness(value);
    }

    /// <summary>
    /// 创建顺序（从 1 开始）。由 <see cref="AnnotationDocument"/> 分配；未加入文档时为 0。
    /// </summary>
    public long CreationOrder { get; internal set; }

    /// <summary>
    /// 几何包围盒（不含描边宽度）。具体含义：
    /// 矩形/椭圆/马赛克是区域；箭头/直线是两端点构成的矩形；画笔是点集的包围盒；
    /// 文字是锚点 + 估算的文字块尺寸。退化图形（单点、零长度线段、零宽区域）
    /// 返回零尺寸矩形（此时 <see cref="PixelRect.IsEmpty"/> 为真），命中测试不依赖它。
    /// </summary>
    public abstract PixelRect Bounds { get; }

    /// <summary>
    /// 渲染裁剪用的包围盒：在 <see cref="Bounds"/> 基础上按粗细的半宽向外扩（至少 1 像素），
    /// 保证描边不会被裁掉。
    /// </summary>
    public virtual PixelRect GetRenderBounds()
    {
        var bounds = Bounds;
        var padding = Math.Max(1, (Thickness + 1) / 2);
        return new PixelRect(
            bounds.Left - padding,
            bounds.Top - padding,
            bounds.Width + padding * 2,
            bounds.Height + padding * 2);
    }

    /// <summary>点是否命中本标注（容差像素，负值按 0 处理）。</summary>
    public bool HitTest(int x, int y, int tolerance = 0) => HitTest(new PixelPoint(x, y), tolerance);

    /// <summary>点是否命中本标注（容差像素，负值按 0 处理）。</summary>
    public abstract bool HitTest(PixelPoint point, int tolerance);

    /// <summary>按给定偏移量平移。</summary>
    public abstract void Move(int dx, int dy);

    /// <summary>是否支持 <see cref="Resize"/>。区域型标注为真，线条/画笔/文字为假。</summary>
    public virtual bool CanResize => false;

    /// <summary>
    /// 调整到新的包围盒。仅 <see cref="CanResize"/> 为真的类型支持；
    /// 不支持时抛 <see cref="NotSupportedException"/>。
    /// </summary>
    public virtual void Resize(PixelRect bounds) =>
        throw new NotSupportedException($"{Kind} 标注不支持调整大小。");

    public override string ToString() => $"{Kind} {Id:N} {Bounds}";
}
