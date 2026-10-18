using ZeepCut.Core.Geometry;

namespace ZeepCut.Core.Capture;

/// <summary>
/// 窗口候选的纯逻辑处理：裁剪到目标屏幕、按 Z 序命中、物理像素 ↔ DIP 映射。
/// 不依赖 WPF 或 Win32，便于在无桌面环境下用 xUnit 覆盖。
/// </summary>
public static class WindowCandidateSelector
{
    /// <summary>
    /// 把候选逐个裁剪到 <paramref name="screen"/>，丢弃空交集，保持输入顺序（Z 序不变）。
    /// 跨屏窗口只保留落在目标屏幕内的部分；完全在屏幕外的候选不会被返回。
    /// </summary>
    public static IReadOnlyList<WindowCandidate> ClipToScreen(
        IEnumerable<WindowCandidate> candidates,
        PixelRect screen)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        if (screen.IsEmpty)
        {
            throw new ArgumentException("目标屏幕不能为空。", nameof(screen));
        }

        var clipped = new List<WindowCandidate>();

        foreach (var candidate in candidates)
        {
            if (candidate is null)
            {
                continue;
            }

            var intersection = candidate.Bounds.Intersect(screen);
            if (intersection.IsEmpty)
            {
                continue;
            }

            clipped.Add(candidate with { Bounds = intersection });
        }

        return clipped;
    }

    /// <summary>
    /// 按 Z 序（列表顺序）命中：返回第一个包含该物理像素点的候选；没有命中时返回 <c>null</c>。
    /// </summary>
    public static WindowCandidate? HitTest(IReadOnlyList<WindowCandidate> candidates, int x, int y)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        foreach (var candidate in candidates)
        {
            if (candidate.Bounds.Contains(x, y))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>
    /// 把相对遮罩窗口左上角的 DIP 指针位置换算为物理像素后命中候选。
    /// </summary>
    /// <remarks>
    /// WPF 鼠标坐标是 DIP，而候选矩形是物理像素：必须先用目标屏幕 DPI 换算，
    /// 再加上屏幕的物理原点（副屏可为负坐标）。直接拿 DIP 去比较物理矩形会在
    /// 125%/150% 缩放下错位。
    /// </remarks>
    public static WindowCandidate? HitTestDip(
        IReadOnlyList<WindowCandidate> candidates,
        double dipX,
        double dipY,
        PixelRect screen,
        int dpi)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        if (screen.IsEmpty)
        {
            throw new ArgumentException("目标屏幕不能为空。", nameof(screen));
        }

        var transform = new DpiTransform(dpi);
        var physicalX = screen.Left + transform.DipToPixel(dipX);
        var physicalY = screen.Top + transform.DipToPixel(dipY);

        return HitTest(candidates, physicalX, physicalY);
    }

    /// <summary>
    /// 把物理像素矩形换算成相对遮罩窗口左上角的 DIP 矩形（用于绘制高亮边框）。
    /// </summary>
    public static DipRect ToDipBounds(PixelRect bounds, PixelRect screen, int dpi)
    {
        if (screen.IsEmpty)
        {
            throw new ArgumentException("目标屏幕不能为空。", nameof(screen));
        }

        var transform = new DpiTransform(dpi);

        return new DipRect(
            transform.PixelToDip(bounds.Left - screen.Left),
            transform.PixelToDip(bounds.Top - screen.Top),
            transform.PixelToDip(bounds.Width),
            transform.PixelToDip(bounds.Height));
    }
}
