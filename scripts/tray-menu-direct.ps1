# Open the Zeep Cut tray menu without UI Automation (T27/T28 driver helper).
#
# Why: on this desktop the tray icon sometimes reports no accessible name for a while after
# a fresh launch, so scripts/tray-menu.ps1 (which finds the icon by UIA name) bails out.
# The app itself is deterministic: posting WM_TRAYICON with the right mouse-up code to its
# hidden message window pops the real Win32 menu (#32768). Then this script scans the menu
# bitmap for text bands and clicks band -Index, exactly like tray-menu.ps1 does.
#
# Menu order (TrayIcon.BuildMenu):
#   0 截图 | 1 从剪贴板贴图 | 2 显示/隐藏全部贴图 | 3 历史 | 4 设置 | 5 检查更新 | 6 退出
param(
    [int]$Index = 3,
    [switch]$List,
    [string]$Shot = ''
)
$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }

Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @'
using System;
using System.Text;
using System.Runtime.InteropServices;

public static class ZeepCutTrayPost
{
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X; public int Y; }
    public delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr value);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc callback, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr hWnd, StringBuilder text, int maxCount);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT point);
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

[ZeepCutTrayPost]::SetProcessDpiAwarenessContext([IntPtr](-4)) | Out-Null

$pidValue = (Get-Process ZeepCut -ErrorAction SilentlyContinue | Select-Object -First 1).Id
if (-not $pidValue) { Write-Host 'APP NOT RUNNING' -ForegroundColor Red; exit 1 }

$tray = [ZeepCutTrayPost]::FindTrayWindow([uint32]$pidValue)
if ($tray -eq [IntPtr]::Zero) { Write-Host 'TRAY WINDOW NOT FOUND' -ForegroundColor Red; exit 1 }
Write-Host ("TRAY WINDOW 0x{0:X}" -f $tray.ToInt64())

# Park the cursor over the taskbar first: TrackPopupMenu needs a foreground owner.
[ZeepCutTrayPost]::SetCursorPos(1700, 20) | Out-Null
[ZeepCutTrayPost]::PostMessage($tray, [ZeepCutTrayPost]::WM_TRAYICON, [IntPtr]::Zero, [IntPtr][int][ZeepCutTrayPost]::WM_RBUTTONUP) | Out-Null

$menu = [IntPtr]::Zero
for ($i = 0; $i -lt 25; $i++) {
    Start-Sleep -Milliseconds 120
    $menu = [ZeepCutTrayPost]::FindPopupMenu()
    if ($menu -ne [IntPtr]::Zero) { break }
}
if ($menu -eq [IntPtr]::Zero) { Write-Host 'POPUP MENU DID NOT OPEN' -ForegroundColor Red; exit 2 }
Start-Sleep -Milliseconds 350

$r = New-Object ZeepCutTrayPost+RECT
[ZeepCutTrayPost]::GetWindowRect($menu, [ref]$r) | Out-Null
Write-Host ("POPUP MENU 0x{0:X} {1},{2} {3}x{4}" -f $menu.ToInt64(), $r.Left, $r.Top, ($r.Right - $r.Left), ($r.Bottom - $r.Top))

if ($Shot) {
    $screens = [System.Windows.Forms.Screen]::AllScreens
    $bmpShot = New-Object System.Drawing.Bitmap($screens[0].Bounds.Width, $screens[0].Bounds.Height)
    $gShot = [System.Drawing.Graphics]::FromImage($bmpShot)
    $gShot.CopyFromScreen($screens[0].Bounds.Left, $screens[0].Bounds.Top, 0, 0, $bmpShot.Size)
    $gShot.Dispose()
    $bmpShot.Save($Shot, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmpShot.Dispose()
    Write-Host ("SHOT {0}" -f $Shot)
}

# Scan the menu bitmap for text rows (dark pixels) -> bands = menu items.
$bmp = New-Object System.Drawing.Bitmap(($r.Right - $r.Left), ($r.Bottom - $r.Top))
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($r.Left, $r.Top, 0, 0, $bmp.Size)
$g.Dispose()

$bands = New-Object System.Collections.ArrayList
$start = -1
for ($y = 0; $y -lt $bmp.Height; $y++) {
    $dark = 0
    $right = [Math]::Min(170, $bmp.Width)
    for ($x = 10; $x -lt $right; $x++) {
        $c = $bmp.GetPixel($x, $y)
        if (($c.R + $c.G + $c.B) -lt 480) { $dark++ }
    }
    if ($dark -gt 2) { if ($start -lt 0) { $start = $y } }
    elseif ($start -ge 0) { if (($y - $start) -ge 6) { [void]$bands.Add([pscustomobject]@{ Top = $start; Bottom = $y - 1 }) }; $start = -1 }
}
$bmp.Dispose()
if ($start -ge 0) { [void]$bands.Add([pscustomobject]@{ Top = $start; Bottom = ($r.Bottom - $r.Top - 1) }) }

Write-Host ("MENU TEXT BANDS = {0} (order: 0 截图 | 1 从剪贴板贴图 | 2 显示/隐藏全部贴图 | 3 历史 | 4 设置 | 5 检查更新 | 6 退出)" -f $bands.Count)
for ($i = 0; $i -lt $bands.Count; $i++) { Write-Host ("  BAND {0} y={1}..{2}" -f $i, ($r.Top + $bands[$i].Top), ($r.Top + $bands[$i].Bottom)) }

if ($List) {
    # Close the menu without choosing anything.
    [ZeepCutTrayPost]::Click(($r.Left + 30), ($r.Top + 4))
    Write-Host 'LIST ONLY, MENU CLOSED'
    exit 0
}

if ($Index -ge $bands.Count) { Write-Host 'BAND INDEX OUT OF RANGE' -ForegroundColor Red; exit 3 }
$x = $r.Left + 60
$y = $r.Top + [int](($bands[$Index].Top + $bands[$Index].Bottom) / 2)
[ZeepCutTrayPost]::Click($x, $y)
Write-Host ("CLICK MENU BAND {0} at {1},{2}" -f $Index, $x, $y)
exit 0
