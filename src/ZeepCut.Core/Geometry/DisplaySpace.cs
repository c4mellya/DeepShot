namespace ZeepCut.Core.Geometry;

/// <summary>
/// 虚拟桌面上的显示器集合，按物理像素坐标做"点 → 屏幕"查找。
/// </summary>
/// <remarks>
/// 查找规则：
/// <list type="number">
/// <item>点落在某块屏幕的 <see cref="MonitorInfo.Bounds"/> 内（半开区间）即返回该屏幕；</item>
/// <item>都不命中时，返回矩形距离最近的屏幕，用于处理点在屏幕边缘外、屏幕间空隙等情况；</item>
/// <item>距离相同时主屏优先；仍相同则取构造时列表中靠前的屏幕。</item>
/// </list>
/// 负坐标屏幕（例如位于主屏左侧、原点为负）按普通坐标处理，无需特殊分支。
/// </remarks>
public sealed class DisplaySpace
{
    private readonly MonitorInfo[] _monitors;

    /// <param name="monitors">至少一块屏幕，最多一个 <see cref="MonitorInfo.IsPrimary"/>。</param>
    public DisplaySpace(IEnumerable<MonitorInfo> monitors)
    {
        ArgumentNullException.ThrowIfNull(monitors);

        _monitors = monitors.ToArray();
        if (_monitors.Length == 0)
        {
            throw new ArgumentException("至少需要一块屏幕。", nameof(monitors));
        }

        if (_monitors.Count(m => m.IsPrimary) > 1)
        {
            throw new ArgumentException("只能有一块主屏。", nameof(monitors));
        }
    }

    /// <summary>全部屏幕，顺序与构造时一致。</summary>
    public IReadOnlyList<MonitorInfo> Monitors => _monitors;

    /// <summary>主屏；若没有标记主屏则为列表第一块。</summary>
    public MonitorInfo Primary =>
        _monitors.FirstOrDefault(m => m.IsPrimary) ?? _monitors[0];

    /// <summary>
    /// 按物理像素点查找所在屏幕；不在任何屏幕内时返回最近的屏幕。
    /// 边界按半开区间处理，因此两块屏幕相接处的 x=0 稳定归属右/主屏。
    /// </summary>
    public MonitorInfo FindByPoint(int x, int y)
    {
        foreach (var monitor in _monitors)
        {
            if (monitor.Bounds.Contains(x, y))
            {
                return monitor;
            }
        }

        var best = _monitors[0];
        var bestDistance = DistanceSquared(best.Bounds, x, y);

        foreach (var monitor in _monitors.Skip(1))
        {
            var distance = DistanceSquared(monitor.Bounds, x, y);

            if (distance < bestDistance ||
                (distance == bestDistance && monitor.IsPrimary && !best.IsPrimary))
            {
                best = monitor;
                bestDistance = distance;
            }
        }

        return best;
    }

    /// <summary>点到矩形的欧氏距离平方；用 long 避免大坐标溢出。</summary>
    private static long DistanceSquared(PixelRect rect, int x, int y)
    {
        // Bounds 的 Right/Bottom 为开边界，点到该边即视为距离 0。
        long dx = x < rect.Left ? (long)rect.Left - x
            : x > rect.Right ? (long)x - rect.Right
            : 0;
        long dy = y < rect.Top ? (long)rect.Top - y
            : y > rect.Bottom ? (long)y - rect.Bottom
            : 0;

        return (dx * dx) + (dy * dy);
    }
}
