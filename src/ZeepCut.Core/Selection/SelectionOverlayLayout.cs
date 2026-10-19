using System.Globalization;
using ZeepCut.Core.Geometry;

namespace ZeepCut.Core.Selection;

/// <summary>
/// 选区浮层的纯逻辑布局：尺寸文字（物理像素）与工具栏/尺寸标签的摆放。
/// 输入输出都是相对遮罩窗口左上角的 DIP，不依赖 WPF，便于在无桌面环境下单测。
/// </summary>
/// <remarks>
/// 摆放规则：
/// <list type="bullet">
/// <item>工具栏优先放在选区正下方；下方放不下时放到选区上方；两者都放不下时夹在窗口内。</item>
/// <item>尺寸标签优先放在选区右上方；上方放不下时放到选区下方。</item>
/// <item>水平方向一律夹取到窗口内，保证不越出屏幕。</item>
/// </list>
/// </remarks>
public static class SelectionOverlayLayout
{
    /// <summary>尺寸文本用的乘号（U+00D7），两侧各一个空格。</summary>
    public const string SizeSeparator = " × ";

    /// <summary>把物理像素宽高格式化为尺寸文字，例如 <c>640 × 480</c>。</summary>
    public static string FormatSize(int pixelWidth, int pixelHeight)
    {
        if (pixelWidth <= 0 || pixelHeight <= 0)
        {
            return string.Empty;
        }

        return pixelWidth.ToString(CultureInfo.InvariantCulture) +
               SizeSeparator +
               pixelHeight.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>把物理像素选区格式化为尺寸文字；空选区返回空字符串。</summary>
    public static string FormatSize(PixelRect selection) =>
        selection.IsEmpty ? string.Empty : FormatSize(selection.Width, selection.Height);

    /// <summary>
    /// 计算工具栏位置（DIP）：优先选区下方，下方空间不足时放上方；右边缘对齐选区，夹取到窗口内。
    /// 选区或窗口为空时返回 <see cref="DipRect.Empty"/>。
    /// </summary>
    /// <param name="selection">选区的窗口内 DIP 矩形。</param>
    /// <param name="client">遮罩窗口的客户区 DIP 矩形，通常是 (0,0,宽,高)。</param>
    /// <param name="toolbarWidth">工具栏期望宽度（DIP）。</param>
    /// <param name="toolbarHeight">工具栏期望高度（DIP）。</param>
    /// <param name="gap">选区与工具栏之间的间距（DIP）。</param>
    public static DipRect PlaceToolbar(
        DipRect selection,
        DipRect client,
        double toolbarWidth,
        double toolbarHeight,
        double gap = 8)
    {
        if (selection.IsEmpty || client.IsEmpty ||
            toolbarWidth <= 0 || toolbarHeight <= 0 || double.IsNaN(toolbarWidth) || double.IsNaN(toolbarHeight))
        {
            return DipRect.Empty;
        }

        var width = Math.Min(toolbarWidth, client.Width);
        var height = Math.Min(toolbarHeight, client.Height);

        var maxLeft = client.Left + Math.Max(0, client.Width - width);
        var left = Math.Clamp(selection.Right - width, client.Left, maxLeft);

        var maxTop = client.Top + Math.Max(0, client.Height - height);
        var below = selection.Bottom + gap;
        var above = selection.Top - gap - height;

        double top;
        if (below + height <= client.Bottom)
        {
            top = below;
        }
        else if (above >= client.Top)
        {
            top = above;
        }
        else
        {
            // 上下都放不下（选区几乎覆盖整屏）时贴住窗口底边，至少保证完整可见。
            top = maxTop;
        }

        top = Math.Clamp(top, client.Top, maxTop);
        return new DipRect(left, top, width, height);
    }

    /// <summary>
    /// 计算尺寸标签位置（DIP）：优先选区右上方，上方放不下时放到选区下方，水平方向夹取到窗口内。
    /// 选区或窗口为空时返回 <see cref="DipRect.Empty"/>。
    /// </summary>
    public static DipRect PlaceSizeBadge(
        DipRect selection,
        DipRect client,
        double badgeWidth,
        double badgeHeight,
        double gap = 6)
    {
        if (selection.IsEmpty || client.IsEmpty ||
            badgeWidth <= 0 || badgeHeight <= 0 || double.IsNaN(badgeWidth) || double.IsNaN(badgeHeight))
        {
            return DipRect.Empty;
        }

        var width = Math.Min(badgeWidth, client.Width);
        var height = Math.Min(badgeHeight, client.Height);

        var maxLeft = client.Left + Math.Max(0, client.Width - width);
        var left = Math.Clamp(selection.Right - width, client.Left, maxLeft);

        var maxTop = client.Top + Math.Max(0, client.Height - height);
        var above = selection.Top - gap - height;
        var below = selection.Bottom + gap;

        var top = above >= client.Top ? above : below;
        top = Math.Clamp(top, client.Top, maxTop);

        return new DipRect(left, top, width, height);
    }

    /// <summary>
    /// 把窗口内 DIP 矩形换算为屏幕物理像素矩形（用于日志与自动化点击验证）。
    /// </summary>
    /// <param name="dip">相对遮罩窗口左上角的 DIP 矩形。</param>
    /// <param name="screen">目标屏幕的物理像素矩形（副屏为负坐标）。</param>
    /// <param name="dpi">目标屏幕 DPI。</param>
    public static PixelRect ToPhysical(DipRect dip, PixelRect screen, int dpi)
    {
        if (dip.IsEmpty || screen.IsEmpty)
        {
            return PixelRect.Empty;
        }

        var transform = new DpiTransform(dpi);
        var left = screen.Left + transform.DipToPixel(dip.Left);
        var top = screen.Top + transform.DipToPixel(dip.Top);
        var right = screen.Left + transform.DipToPixel(dip.Right);
        var bottom = screen.Top + transform.DipToPixel(dip.Bottom);

        return new PixelRect(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }
}
