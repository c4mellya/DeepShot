using System.Runtime.InteropServices;
using System.Text;
using ZeepCut.Core.Capture;
using ZeepCut.Core.Geometry;
using ZeepCut.Platform;

namespace ZeepCut.Capture;

/// <summary>
/// 按 Z 序枚举目标屏幕上的顶层窗口，产出已裁剪到屏幕的 <see cref="WindowCandidate"/>。
/// </summary>
/// <remarks>
/// <para>
/// <c>EnumWindows</c> 自上而下枚举顶层窗口（第一个最靠前）。过滤规则：
/// 不可见、最小化、<c>WS_EX_TOOLWINDOW</c>、本进程窗口、DWM 标记为 cloaked（例如其他虚拟桌面）、
/// <c>GetWindowRect</c> 失败、宽或高为 0。
/// </para>
/// <para>
/// 边框优先使用 <c>DwmGetWindowAttribute(DWMWA_EXTENDED_FRAME_BOUNDS)</c> 的真实可见边框，
/// 失败时回退 <c>GetWindowRect</c>；最后统一在 Core 里与目标屏幕求交集并丢弃空交集。
/// 枚举必须在遮罩窗口显示之前完成，否则遮罩自身会进入候选（本进程窗口也会被过滤）。
/// </para>
/// </remarks>
public static class WindowEnumerator
{
    private const int TitleCapacity = 512;

    // 委托必须由静态字段保持存活，否则 GC 回收后 EnumWindows 回调会崩溃。
    private static readonly NativeMethods.EnumWindowsProc Callback = OnWindow;

    /// <summary>枚举并返回目标屏幕上的窗口候选，顺序即 Z 序（靠前的窗口在上层）。</summary>
    public static IReadOnlyList<WindowCandidate> Enumerate(PixelRect screen)
    {
        if (screen.IsEmpty)
        {
            throw new ArgumentException("目标屏幕不能为空。", nameof(screen));
        }

        var candidates = new List<WindowCandidate>();
        var handle = GCHandle.Alloc(candidates);

        try
        {
            if (!NativeMethods.EnumWindows(Callback, GCHandle.ToIntPtr(handle)))
            {
                var error = Marshal.GetLastWin32Error();
                if (error != 0)
                {
                    AppLog.Warn("WindowEnumerator", $"EnumWindows 未完成，Win32Error={error}");
                }
            }
        }
        finally
        {
            handle.Free();
        }

        var clipped = WindowCandidateSelector.ClipToScreen(candidates, screen);
        AppLog.Info(
            "WindowEnumerator",
            $"屏幕 {screen}：枚举到候选窗口 {candidates.Count} 个，裁剪到屏幕后 {clipped.Count} 个");

        for (var index = 0; index < clipped.Count; index++)
        {
            AppLog.Info("WindowEnumerator", $"  Z#{index} {clipped[index]}");
        }

        return clipped;
    }

    private static bool OnWindow(IntPtr hwnd, IntPtr lParam)
    {
        if (GCHandle.FromIntPtr(lParam).Target is not List<WindowCandidate> candidates)
        {
            return true;
        }

        if (!NativeMethods.IsWindowVisible(hwnd) || NativeMethods.IsIconic(hwnd))
        {
            return true;
        }

        var extendedStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
        if ((extendedStyle & NativeMethods.WS_EX_TOOLWINDOW) != 0)
        {
            return true;
        }

        NativeMethods.GetWindowThreadProcessId(hwnd, out var processId);
        if (processId == (uint)Environment.ProcessId)
        {
            return true;
        }

        if (!NativeMethods.GetWindowRect(hwnd, out var windowRect))
        {
            return true;
        }

        if (windowRect.Width <= 0 || windowRect.Height <= 0)
        {
            return true;
        }

        if (NativeMethods.DwmGetWindowAttributeInt(
                hwnd,
                NativeMethods.DWMWA_CLOAKED,
                out var cloaked,
                sizeof(int)) == 0 &&
            cloaked != 0)
        {
            return true;
        }

        // DWM 的真实边框不含不可见的缩放边框，更接近用户看到的窗口范围。
        var frame = windowRect;
        if (NativeMethods.DwmGetWindowAttribute(
                hwnd,
                NativeMethods.DWMWA_EXTENDED_FRAME_BOUNDS,
                out var extendedFrame,
                Marshal.SizeOf<NativeMethods.Rect>()) == 0)
        {
            frame = extendedFrame;
        }

        if (frame.Width <= 0 || frame.Height <= 0)
        {
            return true;
        }

        var bounds = new PixelRect(frame.Left, frame.Top, frame.Width, frame.Height);
        candidates.Add(new WindowCandidate(hwnd, bounds, ReadTitle(hwnd)));
        return true;
    }

    private static string ReadTitle(IntPtr hwnd)
    {
        var buffer = new StringBuilder(TitleCapacity);
        var length = NativeMethods.GetWindowText(hwnd, buffer, buffer.Capacity);
        return length > 0 ? buffer.ToString(0, Math.Min(length, buffer.Length)) : string.Empty;
    }
}
