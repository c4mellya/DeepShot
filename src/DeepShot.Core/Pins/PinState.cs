using DeepShot.Core.Geometry;

namespace DeepShot.Core.Pins;

/// <summary>
/// 贴图窗口的纯逻辑状态（T18/T19）：原图尺寸、缩放比例、屏幕物理像素位置、
/// 透明度与锁定标志，以及全部限制规则（最小 40×40、最大 8 倍、透明度下限 20%、
/// 锁定后禁止拖动、缩放保持锚点）。
/// </summary>
/// <remarks>
/// <para>
/// 坐标统一使用**屏幕物理像素**（与 <see cref="PixelRect"/>、标注、导出图一致），
/// 与 WPF 的 DIP 换算交给 UI 层的 <c>DpiTransform</c>；本类型无 WPF/Win32 依赖，可单测。
/// </para>
/// <para>
/// 所有修改方法都返回新的 <see cref="PinState"/>（不可变），不做原地修改，
/// 便于 UI 层"改状态 → 应用物理矩形"一步到位，也便于测试直接比较。
/// </para>
/// <para>
/// 尺寸规则：显示尺寸 = 原图尺寸 × <see cref="Scale"/>，四舍五入到整数像素。
/// 缩放下限由 <see cref="MinimumDisplaySize"/> 反推（宽高都要 ≥ 40 像素，
/// 所以下限取两个方向的较大者）；上限 <see cref="MaximumScale"/> = 8 倍。
/// 原图极小时（例如 2×2）最小显示尺寸优先于 8 倍上限，否则贴图会缩到看不见。
/// </para>
/// </remarks>
public readonly record struct PinState
{
    /// <summary>贴图显示的最小边长（物理像素），宽高都不允许小于它。</summary>
    public const int MinimumDisplaySize = 40;

    /// <summary>最大缩放倍率（相对原图 1:1）。</summary>
    public const double MaximumScale = 8.0;

    /// <summary>透明度的可见下限（20%）：不允许调到完全看不见。</summary>
    public const double MinimumOpacity = 0.2;

    /// <summary>透明度上限（不透明）。</summary>
    public const double MaximumOpacity = 1.0;

    /// <summary>Ctrl+滚轮每档调整的透明度。</summary>
    public const double OpacityStep = 0.1;

    /// <summary>滚轮每档的缩放倍率（向内 / 向外都按同一个系数）。</summary>
    public const double ZoomStep = 1.1;

    private PinState(
        int sourceWidth,
        int sourceHeight,
        double scale,
        PixelPoint topLeft,
        double opacity,
        bool isLocked)
    {
        SourceWidth = sourceWidth;
        SourceHeight = sourceHeight;
        Scale = scale;
        TopLeft = topLeft;
        Opacity = opacity;
        IsLocked = isLocked;
    }

    /// <summary>原图宽度（物理像素，1:1 时的窗口宽度）。</summary>
    public int SourceWidth { get; private init; }

    /// <summary>原图高度（物理像素，1:1 时的窗口高度）。</summary>
    public int SourceHeight { get; private init; }

    /// <summary>当前缩放倍率，范围由 <see cref="MinimumScale"/> 与 <see cref="MaximumScale"/> 决定。</summary>
    public double Scale { get; private init; }

    /// <summary>贴图左上角的屏幕物理像素坐标（副屏可为负）。</summary>
    public PixelPoint TopLeft { get; private init; }

    /// <summary>窗口透明度，范围 [<see cref="MinimumOpacity"/>, <see cref="MaximumOpacity"/>]。</summary>
    public double Opacity { get; private init; }

    /// <summary>是否锁定位置。锁定后仍可缩放/调透明度/右键菜单，只是不能拖动。</summary>
    public bool IsLocked { get; private init; }

    /// <summary>显示宽度（物理像素），不小于 <see cref="MinimumDisplaySize"/>。</summary>
    public int DisplayWidth => Math.Max(MinimumDisplaySize, ToDisplayLength(SourceWidth));

    /// <summary>显示高度（物理像素），不小于 <see cref="MinimumDisplaySize"/>。</summary>
    public int DisplayHeight => Math.Max(MinimumDisplaySize, ToDisplayLength(SourceHeight));

    /// <summary>当前贴图在屏幕上的物理像素矩形。</summary>
    public PixelRect Bounds => new(TopLeft.X, TopLeft.Y, DisplayWidth, DisplayHeight);

    /// <summary>由"宽高都要 ≥ 40 像素"反推的最小缩放倍率。</summary>
    public double MinimumScale =>
        Math.Max(MinimumDisplaySize / (double)SourceWidth, MinimumDisplaySize / (double)SourceHeight);

    /// <summary>是否可以拖动（锁定后为 false）。</summary>
    public bool CanDrag => !IsLocked;

    /// <summary>是否处于原始大小（1:1）。</summary>
    public bool IsOriginalSize => Math.Abs(Scale - 1.0) < 1e-9;

    /// <summary>
    /// 创建初始状态：按原图尺寸 1:1 显示在给定左上角，完全不透明且未锁定。
    /// </summary>
    /// <param name="sourceWidth">原图宽（物理像素），必须为正。</param>
    /// <param name="sourceHeight">原图高（物理像素），必须为正。</param>
    /// <param name="topLeft">屏幕物理像素左上角。</param>
    public static PinState Create(int sourceWidth, int sourceHeight, PixelPoint topLeft)
    {
        if (sourceWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceWidth), sourceWidth, "原图宽度必须为正数。");
        }

        if (sourceHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceHeight), sourceHeight, "原图高度必须为正数。");
        }

        return new PinState(
            sourceWidth,
            sourceHeight,
            ClampScaleFor(sourceWidth, sourceHeight, 1.0),
            topLeft,
            MaximumOpacity,
            isLocked: false);
    }

    /// <summary>把任意缩放倍率夹到本贴图的合法范围。</summary>
    public double ClampScale(double scale) => Math.Clamp(scale, MinimumScale, Math.Max(MinimumScale, MaximumScale));

    /// <summary>按给定倍率缩放，并保持 <paramref name="anchor"/> 这个屏幕像素点不动（鼠标位置锚点）。</summary>
    /// <remarks>
    /// 锚点相对旧矩形的比例会被夹到 [0,1]：鼠标在窗口外时按最近的边处理，不会把贴图甩飞。
    /// 倍率非法（NaN/Infinity/≤0）或已在上/下限时返回原状态。
    /// </remarks>
    public PinState ScaleTo(double scale, PixelPoint anchor)
    {
        if (!double.IsFinite(scale) || scale <= 0)
        {
            return this;
        }

        var clamped = ClampScale(scale);
        if (Math.Abs(clamped - Scale) < 1e-9)
        {
            return this;
        }

        var width = Math.Max(MinimumDisplaySize, ToDisplayLength(SourceWidth, clamped));
        var height = Math.Max(MinimumDisplaySize, ToDisplayLength(SourceHeight, clamped));

        var old = Bounds;

        // 锚点先夹进旧矩形：鼠标在窗口外（例如滚轮时指针刚好移出边界）按最近的边锁定，
        // 而不是按"指针到窗口的距离"把窗口整体甩飞。
        var anchorX = Math.Clamp(anchor.X, old.Left, old.Right);
        var anchorY = Math.Clamp(anchor.Y, old.Top, old.Bottom);
        var fractionX = old.Width > 0 ? Math.Clamp((anchorX - old.Left) / (double)old.Width, 0.0, 1.0) : 0.5;
        var fractionY = old.Height > 0 ? Math.Clamp((anchorY - old.Top) / (double)old.Height, 0.0, 1.0) : 0.5;

        var left = (int)Math.Round(anchorX - fractionX * width, MidpointRounding.AwayFromZero);
        var top = (int)Math.Round(anchorY - fractionY * height, MidpointRounding.AwayFromZero);

        return this with
        {
            Scale = clamped,
            TopLeft = new PixelPoint(left, top),
        };
    }

    /// <summary>按系数缩放（&gt;1 放大、&lt;1 缩小），保持锚点不动。</summary>
    public PinState ZoomBy(double factor, PixelPoint anchor) => ScaleTo(Scale * factor, anchor);

    /// <summary>恢复原始大小（1:1），保持当前中心点不动，避免贴图突然跳走。</summary>
    public PinState ResetToOriginal()
    {
        var bounds = Bounds;
        var center = new PixelPoint(
            bounds.Left + (bounds.Width / 2),
            bounds.Top + (bounds.Height / 2));
        return ScaleTo(1.0, center);
    }

    /// <summary>设置透明度；越界静默夹到 [<see cref="MinimumOpacity"/>, <see cref="MaximumOpacity"/>]。</summary>
    public PinState WithOpacity(double opacity)
    {
        var value = double.IsFinite(opacity) ? opacity : Opacity;
        var clamped = Math.Clamp(value, MinimumOpacity, MaximumOpacity);
        return Math.Abs(clamped - Opacity) < 1e-9 ? this : this with { Opacity = clamped };
    }

    /// <summary>按增量调整透明度（Ctrl+滚轮），并夹在可见范围内。</summary>
    public PinState AdjustOpacity(double delta) => WithOpacity(Opacity + delta);

    /// <summary>设置锁定状态。</summary>
    public PinState WithLock(bool isLocked) => IsLocked == isLocked ? this : this with { IsLocked = isLocked };

    /// <summary>切换锁定状态。</summary>
    public PinState ToggleLock() => WithLock(!IsLocked);

    /// <summary>
    /// 拖动到新的左上角。锁定状态下**拒绝移动**并原样返回（T19 的锁定语义）。
    /// </summary>
    public PinState DragTo(PixelPoint topLeft) =>
        IsLocked || topLeft == TopLeft ? this : this with { TopLeft = topLeft };

    /// <summary>拖动到新的左上角，并报告是否真的移动了（锁定/位置相同时为 false）。</summary>
    public bool TryDragTo(PixelPoint topLeft, out PinState result)
    {
        result = DragTo(topLeft);
        return result.TopLeft != TopLeft;
    }

    /// <summary>按偏移量拖动；锁定状态下拒绝移动。</summary>
    public PinState DragBy(int dx, int dy) => DragTo(new PixelPoint(TopLeft.X + dx, TopLeft.Y + dy));

    /// <summary>把贴图放到指定左上角（不检查锁定；用于程序化摆放、初始定位）。</summary>
    public PinState PlaceAt(PixelPoint topLeft) => this with { TopLeft = topLeft };

    public override string ToString() =>
        $"{Bounds} 原图={SourceWidth}x{SourceHeight} 缩放={Scale:0.###}x 透明度={Opacity:0.##} " +
        $"锁定={IsLocked}";

    /// <summary>对任意原图尺寸计算夹取后的缩放倍率（用于静态构造，不需要实例）。</summary>
    public static double ClampScaleFor(int sourceWidth, int sourceHeight, double scale)
    {
        if (sourceWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceWidth), sourceWidth, "原图宽度必须为正数。");
        }

        if (sourceHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceHeight), sourceHeight, "原图高度必须为正数。");
        }

        var minimum = Math.Max(
            MinimumDisplaySize / (double)sourceWidth,
            MinimumDisplaySize / (double)sourceHeight);
        return Math.Clamp(scale, minimum, Math.Max(minimum, MaximumScale));
    }

    private int ToDisplayLength(int sourceLength) => ToDisplayLength(sourceLength, Scale);

    private static int ToDisplayLength(int sourceLength, double scale) =>
        (int)Math.Round(sourceLength * scale, MidpointRounding.AwayFromZero);
}
