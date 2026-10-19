using DeepShot.Core.Geometry;

namespace DeepShot.Core.Capture;

/// <summary>
/// 一个可被悬停识别的顶层窗口候选。
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Bounds"/> 是物理像素坐标，且已裁剪到目标屏幕；跨屏窗口只保留落在目标屏幕内的部分。
/// <see cref="Handle"/> 是 Win32 <c>HWND</c>，只作为标识，不参与几何计算。
/// </para>
/// <para>
/// 候选列表的顺序就是 Z 序：靠前的窗口在更上层，命中时先取靠前的。
/// </para>
/// </remarks>
/// <param name="Handle">窗口句柄（<c>HWND</c>）。</param>
/// <param name="Bounds">已裁剪到目标屏幕的物理像素矩形。</param>
/// <param name="Title">窗口标题，用于日志与验证；可以为空。</param>
public sealed record WindowCandidate(IntPtr Handle, PixelRect Bounds, string Title = "")
{
    public override string ToString() =>
        string.IsNullOrEmpty(Title)
            ? $"0x{Handle.ToInt64():X} {Bounds}"
            : $"0x{Handle.ToInt64():X} {Bounds} \"{Title}\"";
}
