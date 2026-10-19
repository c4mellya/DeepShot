# Open the Zeep Cut tray menu (PostMessage WM_TRAYICON), screenshot the menu rect, then close it.
# Same technique as tray-menu-direct.ps1 but without the broken [System.Windows.Forms.Screen]
# dependency (that type is never loaded there, so -Shot always crashed and left the menu open).
param(
    [Parameter(Mandatory = $true)][string]$Out,
    [switch]$KeepOpen
)
$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }
Add-Type -AssemblyName System.Drawing

Add-Type -TypeDefinition @'
using System;
using System.Text;
using System.Runtime.InteropServices;

public static class ZeepCutTrayShot
{
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    public delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr value);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc callback, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr hWnd, StringBuilder text, int maxCount);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, UIntPtr e);

    public const uint WM_TRAYICON = 0x8000 + 1;
    public const uint WM_RBUTTONUP = 0x0205;

    public static string ClassOf(IntPtr h) { var s = new StringBuilder(256); GetClassName(h, s, 256); return s.ToString(); }

    public static IntPtr FindTrayWindow(uint pid)
    {
        IntPtr found = IntPtr.Zero;
        EnumWindows(delegate (IntPtr h, IntPtr l)
        {
            uint p; GetWindowThreadProcessId(h, out p);
            if (p == pid && ClassOf(h) == "ZeepCut.TrayMessageWindow") { found = h; return false; }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    public static IntPtr FindPopupMenu()
    {
        IntPtr found = IntPtr.Zero;
        EnumWindows(delegate (IntPtr h, IntPtr l)
        {
            if (IsWindowVisible(h) && ClassOf(h) == "#32768") { found = h; }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    public static void Click(int x, int y)
    {
        SetCursorPos(x, y);
        System.Threading.Thread.Sleep(120);
        mouse_event(0x0002, 0, 0, 0, UIntPtr.Zero);
        System.Threading.Thread.Sleep(90);
        mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero);
    }
}
'@

[ZeepCutTrayShot]::SetProcessDpiAwarenessContext([IntPtr](-4)) | Out-Null

$proc = Get-Process ZeepCut -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $proc) { Write-Host 'APP NOT RUNNING' -ForegroundColor Red; exit 1 }
$tray = [ZeepCutTrayShot]::FindTrayWindow([uint32]$proc.Id)
if ($tray -eq [IntPtr]::Zero) { Write-Host 'TRAY WINDOW NOT FOUND' -ForegroundColor Red; exit 1 }

[ZeepCutTrayShot]::SetCursorPos(1700, 20) | Out-Null
[ZeepCutTrayShot]::PostMessage($tray, [ZeepCutTrayShot]::WM_TRAYICON, [IntPtr]::Zero, [IntPtr][int][ZeepCutTrayShot]::WM_RBUTTONUP) | Out-Null

$menu = [IntPtr]::Zero
for ($i = 0; $i -lt 25; $i++) {
    Start-Sleep -Milliseconds 120
    $menu = [ZeepCutTrayShot]::FindPopupMenu()
    if ($menu -ne [IntPtr]::Zero) { break }
}
if ($menu -eq [IntPtr]::Zero) { Write-Host 'POPUP MENU DID NOT OPEN' -ForegroundColor Red; exit 2 }
Start-Sleep -Milliseconds 400

$r = New-Object ZeepCutTrayShot+RECT
[ZeepCutTrayShot]::GetWindowRect($menu, [ref]$r) | Out-Null
$w = $r.Right - $r.Left
$h = $r.Bottom - $r.Top
Write-Host ("POPUP MENU {0},{1} {2}x{3}" -f $r.Left, $r.Top, $w, $h)

$bmp = New-Object System.Drawing.Bitmap($w, $h)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($r.Left, $r.Top, 0, 0, $bmp.Size)
$g.Dispose()
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
Write-Host "SAVED $Out"

if (-not $KeepOpen) {
    # Close without choosing: press Esc into the popup menu.
    [ZeepCutTrayShot]::Click(($r.Left + 30), ($r.Top + 4))
    Write-Host 'MENU CLOSED'
}
exit 0
