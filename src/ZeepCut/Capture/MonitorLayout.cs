using System.Runtime.InteropServices;
using ZeepCut.Core.Geometry;
using ZeepCut.Platform;

namespace ZeepCut.Capture;

/// <summary>
/// 屏幕集合的读取（T21 用）：<c>EnumDisplayMonitors</c> + <c>GetMonitorInfo</c> +
/// <c>GetDpiForMonitor</c> 得到每块屏幕的物理矩形、工作区与 DPI，供 Core 的
/// <see cref="DisplaySpace"/> 做"点 → 屏幕"查找（副屏原点可为负）。
/// </summary>
public static class MonitorLayout
{
    /// <summary>枚举当前所有屏幕；失败时返回空列表（调用方需要兜底）。</summary>
    public static IReadOnlyList<MonitorInfo> ReadAll()
    {
        var monitors = new List<MonitorInfo>();
        var callback = new NativeMethods.MonitorEnumProc((handle, _, _, _) =>
        {
            if (TryDescribe(handle, out var info))
            {
                monitors.Add(info);
            }

            return true;
        });

        if (!NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero))
        {
            var error = Marshal.GetLastWin32Error();
            AppLog.Warn("Display", $"EnumDisplayMonitors 失败（Win32Error={error}），将退回主屏");
        }

        // 委托必须活到枚举结束；上面的同步调用已经返回，这里只是提醒不要改成异步。
        GC.KeepAlive(callback);
        return monitors;
    }

    /// <summary>
    /// 读取所有屏幕并构造 <see cref="DisplaySpace"/>；一块都读不到时用主屏度量兜底。
    /// </summary>
    public static DisplaySpace ReadDisplaySpace()
    {
        var monitors = ReadAll();
        if (monitors.Count == 0)
        {
            var fallback = ReadVirtualScreenFallback();
            AppLog.Warn("Display", $"未能枚举到屏幕，使用兜底屏幕 {fallback.Bounds} DPI={fallback.Dpi}");
            return new DisplaySpace(new[] { fallback });
        }

        return new DisplaySpace(monitors);
    }

    private static bool TryDescribe(IntPtr handle, out MonitorInfo info)
    {
        info = null!;

        var raw = new NativeMethods.MonitorInfoEx
        {
            cbSize = Marshal.SizeOf<NativeMethods.MonitorInfoEx>(),
        };

        if (!NativeMethods.GetMonitorInfo(handle, ref raw))
        {
            return false;
        }

        var bounds = new PixelRect(
            raw.rcMonitor.Left,
            raw.rcMonitor.Top,
            raw.rcMonitor.Width,
            raw.rcMonitor.Height);

        var work = new PixelRect(
            raw.rcWork.Left,
            raw.rcWork.Top,
            raw.rcWork.Width,
            raw.rcWork.Height);

        if (bounds.IsEmpty)
        {
            return false;
        }

        if (work.IsEmpty)
        {
            work = bounds;
        }

        var dpi = ScreenCapture.TryGetMonitorDpi(handle, out var detected) ? detected : 96;
        info = new MonitorInfo(
            string.IsNullOrWhiteSpace(raw.szDevice) ? "monitor" : raw.szDevice,
            bounds,
            work,
            dpi,
            (raw.dwFlags & NativeMethods.MONITORINFOF_PRIMARY) != 0);
        return true;
    }

    /// <summary>极端兜底：用 GetSystemMetrics 的虚拟桌面矩形 + 96 DPI。</summary>
    private static MonitorInfo ReadVirtualScreenFallback()
    {
        var left = NativeMethods.GetSystemMetrics(NativeMethods.SM_XVIRTUALSCREEN);
        var top = NativeMethods.GetSystemMetrics(NativeMethods.SM_YVIRTUALSCREEN);
        var width = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXVIRTUALSCREEN);
        var height = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYVIRTUALSCREEN);

        if (width <= 0 || height <= 0)
        {
            width = 1024;
            height = 768;
        }

        var bounds = new PixelRect(left, top, width, height);
        return new MonitorInfo("fallback", bounds, bounds, 96, isPrimary: true);
    }
}
