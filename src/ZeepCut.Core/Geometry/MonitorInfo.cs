namespace ZeepCut.Core.Geometry;

/// <summary>
/// 单个显示器的信息，坐标统一为物理像素（与 Win32 的虚拟桌面坐标系一致）。
/// </summary>
public sealed record MonitorInfo
{
    /// <param name="deviceName">屏幕标识，例如 <c>\\.\DISPLAY1</c>。</param>
    /// <param name="bounds">整块屏幕的物理像素矩形，可为负坐标。</param>
    /// <param name="workArea">去掉任务栏等后的可用工作区（物理像素）。</param>
    /// <param name="dpi">屏幕 DPI，96 = 100%，120 = 125%，144 = 150%。</param>
    /// <param name="isPrimary">是否为系统主屏。一个 <see cref="DisplaySpace"/> 中至多一个主屏。</param>
    public MonitorInfo(string deviceName, PixelRect bounds, PixelRect workArea, int dpi, bool isPrimary)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceName);
        if (bounds.IsEmpty)
        {
            throw new ArgumentException("屏幕物理边界不能为空。", nameof(bounds));
        }

        if (workArea.IsEmpty)
        {
            throw new ArgumentException("屏幕工作区不能为空。", nameof(workArea));
        }

        if (dpi <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(dpi), dpi, "DPI 必须为正数。");
        }

        DeviceName = deviceName;
        Bounds = bounds;
        WorkArea = workArea;
        Dpi = dpi;
        IsPrimary = isPrimary;
    }

    /// <summary>屏幕标识，例如 <c>\\.\DISPLAY1</c>。</summary>
    public string DeviceName { get; }

    /// <summary>整块屏幕的物理像素矩形（可为负坐标）。</summary>
    public PixelRect Bounds { get; }

    /// <summary>工作区（任务栏之外）的物理像素矩形。</summary>
    public PixelRect WorkArea { get; }

    /// <summary>屏幕 DPI（96/120/144 分别对应 100%/125%/150%）。</summary>
    public int Dpi { get; }

    /// <summary>是否为系统主屏。</summary>
    public bool IsPrimary { get; }

    /// <summary>DPI 缩放比例，<see cref="Dpi"/> / 96。</summary>
    public double Scale => Dpi / 96.0;
}
