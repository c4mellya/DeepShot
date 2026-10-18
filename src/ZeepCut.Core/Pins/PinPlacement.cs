using ZeepCut.Core.Geometry;

namespace ZeepCut.Core.Pins;

/// <summary>
/// 程序化摆放贴图的纯逻辑（T21）：把"从剪贴板贴出的图"放到光标附近，
/// 并保证不越出光标所在屏幕。
/// </summary>
/// <remarks>
/// <para>
/// 位置规则（本类型只算位置，不改尺寸）：
/// <list type="number">
/// <item>默认放在光标右下方，偏移 <see cref="CursorOffset"/> 像素；</item>
/// <item>右/下放不下时，分别翻到光标左侧/上方（两个方向独立判断），
/// 这样靠近右下角时贴图不会只露出一条边；</item>
/// <item>最后用 <see cref="PixelRect.Clamp"/> 夹进屏幕：仍越界时平移回屏幕内，
/// 贴图比屏幕还大时贴左上角（超出部分允许伸出屏幕，与 T18 的 8 倍缩放一致）。</item>
/// </list>
/// </para>
/// <para>
/// 屏幕查找由调用方用 <see cref="DisplaySpace"/> 完成（本机副屏原点为负，不能假设 0,0）。
/// </para>
/// </remarks>
public static class PinPlacement
{
    /// <summary>贴图与光标之间的默认间距（物理像素）。</summary>
    public const int CursorOffset = 16;

    /// <summary>
    /// 计算贴图左上角：光标附近且保持在 <paramref name="screenBounds"/> 内。
    /// </summary>
    /// <param name="cursor">光标屏幕物理坐标。</param>
    /// <param name="width">贴图显示宽度（物理像素），必须为正。</param>
    /// <param name="height">贴图显示高度（物理像素），必须为正。</param>
    /// <param name="screenBounds">光标所在屏幕的物理矩形（可为负坐标）。</param>
    /// <returns>贴图的屏幕物理矩形（尺寸等于入参，屏幕装不下时按屏幕尺寸缩小）。</returns>
    public static PixelRect NearCursor(PixelPoint cursor, int width, int height, PixelRect screenBounds)
    {
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), width, "贴图宽度必须为正数。");
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height), height, "贴图高度必须为正数。");
        }

        if (screenBounds.IsEmpty)
        {
            throw new ArgumentException("屏幕矩形不能为空。", nameof(screenBounds));
        }

        // 优先右下：右侧放不下就翻到光标左边，下侧放不下就翻到光标上方。
        var left = cursor.X + CursorOffset;
        if (left + width > screenBounds.Right)
        {
            left = cursor.X - CursorOffset - width;
        }

        var top = cursor.Y + CursorOffset;
        if (top + height > screenBounds.Bottom)
        {
            top = cursor.Y - CursorOffset - height;
        }

        return new PixelRect(left, top, width, height).Clamp(screenBounds);
    }

    /// <summary>在 <paramref name="display"/> 中找到 <paramref name="cursor"/> 所在屏幕，再按它摆放。</summary>
    /// <param name="display">虚拟桌面上的屏幕集合（含负坐标屏幕）。</param>
    public static PixelRect NearCursor(
        DisplaySpace display,
        PixelPoint cursor,
        int width,
        int height)
    {
        ArgumentNullException.ThrowIfNull(display);
        var monitor = display.FindByPoint(cursor.X, cursor.Y);
        return NearCursor(cursor, width, height, monitor.Bounds);
    }
}
