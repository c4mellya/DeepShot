# Inspect (and optionally click) the Zeep Cut tray icon through the Windows shell.
# Uses the hidden window created by the app (class ZeepCut.TrayMessageWindow).
# Exit codes: 0 = icon rect found, 1 = tray window not found, 2 = rect unavailable.
param(
    [switch]$Click,
    [ValidateSet('left','right')][string]$Button = 'right'
)
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

public static class ZeepCutTray
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct NOTIFYICONIDENTIFIER
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public Guid guidItem;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

    [DllImport("shell32.dll")]
    public static extern int Shell_NotifyIconGetRect(ref NOTIFYICONIDENTIFIER identifier, out RECT iconLocation);

    [DllImport("user32.dll")]
    public static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    public static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);

    public const uint LEFTDOWN = 0x0002;
    public const uint LEFTUP = 0x0004;
    public const uint RIGHTDOWN = 0x0008;
    public const uint RIGHTUP = 0x0010;

    public static IntPtr FindTrayWindow()
    {
        return FindWindow("ZeepCut.TrayMessageWindow", null);
    }

    public static int GetIconRect(out int left, out int top, out int right, out int bottom)
    {
        left = top = right = bottom = 0;
        IntPtr hwnd = FindTrayWindow();
        if (hwnd == IntPtr.Zero)
        {
            return unchecked((int)0x80004005);
        }

        var id = new NOTIFYICONIDENTIFIER
        {
            cbSize = (uint)Marshal.SizeOf(typeof(NOTIFYICONIDENTIFIER)),
            hWnd = hwnd,
            uID = 1
        };
        RECT rect;
        int hr = Shell_NotifyIconGetRect(ref id, out rect);
        left = rect.Left;
        top = rect.Top;
        right = rect.Right;
        bottom = rect.Bottom;
        return hr;
    }

    public static void ClickAt(int x, int y, bool right)
    {
        SetCursorPos(x, y);
        System.Threading.Thread.Sleep(200);
        if (right)
        {
            mouse_event(RIGHTDOWN, 0, 0, 0, UIntPtr.Zero);
            mouse_event(RIGHTUP, 0, 0, 0, UIntPtr.Zero);
        }
        else
        {
            mouse_event(LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
            mouse_event(LEFTUP, 0, 0, 0, UIntPtr.Zero);
        }
    }
}
'@

$hwnd = [ZeepCutTray]::FindTrayWindow()
if ($hwnd -eq [IntPtr]::Zero) {
    Write-Host 'TRAY WINDOW NOT FOUND (is ZeepCut running?)' -ForegroundColor Red
    exit 1
}
Write-Host ("TRAY WINDOW hwnd=0x{0:X}" -f $hwnd.ToInt64())

$left = 0; $top = 0; $right = 0; $bottom = 0
$hr = [ZeepCutTray]::GetIconRect([ref]$left, [ref]$top, [ref]$right, [ref]$bottom)
Write-Host ("Shell_NotifyIconGetRect hr=0x{0:X8} rect=({1},{2})-({3},{4})" -f $hr, $left, $top, $right, $bottom)
if ($hr -ne 0) {
    Write-Host 'ICON RECT UNAVAILABLE (icon may sit in the overflow flyout)' -ForegroundColor Yellow
    exit 2
}

$cx = [int](($left + $right) / 2)
$cy = [int](($top + $bottom) / 2)
Write-Host ("TRAY ICON CENTER {0},{1}" -f $cx, $cy)

if ($Click) {
    [ZeepCutTray]::ClickAt($cx, $cy, ($Button -eq 'right'))
    Write-Host ("CLICKED {0} at {1},{2}" -f $Button, $cx, $cy)
}
