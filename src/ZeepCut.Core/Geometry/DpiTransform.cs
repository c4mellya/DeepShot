namespace ZeepCut.Core.Geometry;

/// <summary>
/// 物理像素与 WPF DIP 的双向换算，scale = dpi / 96。
/// </summary>
/// <remarks>
/// <para>
/// WPF 在 PerMonitorV2 下的 Left/Top/Width/Height 是 DIP，而屏幕捕获、图片裁剪和
/// Win32 矩形都是物理像素；两者不能混用。本类型是唯一的换算入口。
/// </para>
/// <para>
/// 舍入规则：DIP → 像素按"四舍五入、.5 远离零"取整。像素 → DIP 保留 double 精度。
/// 整数像素先转到 DIP 再转回时结果一致（例如 125% 下 3 px → 2.4 DIP → 3 px）。
/// </para>
/// </remarks>
public readonly struct DpiTransform
{
    /// <param name="dpi">屏幕 DPI，必须为正数（96/120/144 …）。</param>
    public DpiTransform(int dpi)
    {
        if (dpi <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(dpi), dpi, "DPI 必须为正数。");
        }

        Dpi = dpi;
    }

    /// <summary>屏幕 DPI（96 = 100%，120 = 125%，144 = 150%）。</summary>
    public int Dpi { get; }

    /// <summary>缩放比例：dpi / 96。</summary>
    public double Scale => Dpi / 96.0;

    /// <summary>由屏幕信息创建换算器。</summary>
    public static DpiTransform FromMonitor(MonitorInfo monitor)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        return new DpiTransform(monitor.Dpi);
    }

    /// <summary>物理像素 → DIP。</summary>
    public double PixelToDip(int pixel) => pixel / Scale;

    /// <summary>物理像素 → DIP（接受小数像素，例如矩形中心点）。</summary>
    public double PixelToDip(double pixel) => pixel / Scale;

    /// <summary>DIP → 物理像素，四舍五入、.5 远离零。</summary>
    public int DipToPixel(double dip) => (int)Math.Round(dip * Scale, MidpointRounding.AwayFromZero);

    /// <summary>把物理像素矩形整体换算为 DIP 矩形，四条边分别换算以保持边界对齐。</summary>
    public DipRect ToDip(PixelRect rect) => rect.IsEmpty
        ? DipRect.Empty
        : new DipRect(
            PixelToDip(rect.Left),
            PixelToDip(rect.Top),
            PixelToDip(rect.Width),
            PixelToDip(rect.Height));

    /// <summary>
    /// 把 DIP 矩形换算为物理像素矩形。分别对左/上/右/下取整后反推宽高，
    /// 避免"宽高各自取整"导致的边界累积误差（例如 100 DIP 在 125% 下为 125 px）。
    /// </summary>
    public PixelRect ToPixels(DipRect rect)
    {
        if (rect.IsEmpty)
        {
            return PixelRect.Empty;
        }

        var left = DipToPixel(rect.Left);
        var top = DipToPixel(rect.Top);
        var right = DipToPixel(rect.Right);
        var bottom = DipToPixel(rect.Bottom);

        return new PixelRect(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }
}
