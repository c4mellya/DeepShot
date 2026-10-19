using System.Runtime.InteropServices;

namespace ZeepCut.Platform;

/// <summary>Use supported desktop topmost and shell fullscreen APIs for the capture overlay.</summary>
internal static class CaptureWindowLayer
{
    public static void Raise(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        // Reinsert at the top of the topmost band without stealing input or changing geometry.
        if (!NativeMethods.SetWindowPos(hwnd, new IntPtr(-1), 0, 0, 0, 0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE))
            AppLog.Warn("Overlay", $"重新置顶失败：{Marshal.GetLastWin32Error()}");
    }

    public static void MarkFullscreen(IntPtr hwnd, bool fullscreen)
    {
        if (hwnd == IntPtr.Zero) return;
        object? shell = null;
        try
        {
            var type = Type.GetTypeFromCLSID(new Guid("56FDF344-FD6D-11D0-958A-006097C9A090"));
            if (type is null) return;
            shell = Activator.CreateInstance(type);
            if (shell is not ITaskbarList2 taskbar) return;
            Marshal.ThrowExceptionForHR(taskbar.HrInit());
            Marshal.ThrowExceptionForHR(taskbar.MarkFullscreenWindow(hwnd, fullscreen));
        }
        catch (Exception ex)
        {
            // Fullscreen is a shell hint, not permission to override protected notification bands.
            AppLog.Warn("Overlay", $"设置 Shell 全屏状态失败（置顶仍有效）：{ex.Message}");
        }
        finally
        {
            if (shell is not null && Marshal.IsComObject(shell)) Marshal.ReleaseComObject(shell);
        }
    }

    [ComImport, Guid("602D4995-B13A-429B-A66E-1935E44F4317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITaskbarList2
    {
        [PreserveSig] int HrInit();
        [PreserveSig] int AddTab(IntPtr hwnd);
        [PreserveSig] int DeleteTab(IntPtr hwnd);
        [PreserveSig] int ActivateTab(IntPtr hwnd);
        [PreserveSig] int SetActiveAlt(IntPtr hwnd);
        [PreserveSig] int MarkFullscreenWindow(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool fullscreen);
    }
}
