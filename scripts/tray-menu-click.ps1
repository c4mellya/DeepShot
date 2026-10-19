# Click a Zeep Cut tray menu item chosen by its TEXT (not by band index).
#
# Why: T31 made the tray menu bilingual, so "band 5 = 检查更新" is no longer stable
# (and an off-by-one click on "Exit" kills the app). This helper opens the real Win32 menu,
# renders it with DrawMenuBar-free "owner draw" style into a bitmap, and matches each text
# band against the strings the app writes to its log ("托盘菜单文案：…… 设置=Settings ...").
#
# Usage:
#   tray-menu-click.ps1 -Text 'Check for updates'
#   tray-menu-click.ps1 -Text 'Settings' -Shot out.png
param(
    [Parameter(Mandatory = $true)][string]$Text,
    [string]$Shot = ''
)
$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }
Add-Type -AssemblyName System.Drawing

Add-Type -TypeDefinition @'
using System;
using System.Text;
using System.Runtime.InteropServices;

public static class ZeepCutTrayClick
{
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    public delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr value);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc callback, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr hWnd, StringBuilder text, int maxCount);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, UIntPtr e);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);

    /// <summary>把（可能被全屏遮罩盖住的）Zeep Cut 窗口提到前台：先 ShowWindow 再 SetForegroundWindow。</summary>
    public static void BringToFront(IntPtr hWnd)
    {
        ShowWindow(hWnd, 9);
        SetForegroundWindow(hWnd);
    }

    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }

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

[ZeepCutTrayClick]::SetProcessDpiAwarenessContext([IntPtr](-4)) | Out-Null

$proc = Get-Process ZeepCut -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $proc) { Write-Host 'APP NOT RUNNING' -ForegroundColor Red; exit 1 }
$tray = [ZeepCutTrayClick]::FindTrayWindow([uint32]$proc.Id)
if ($tray -eq [IntPtr]::Zero) { Write-Host 'TRAY WINDOW NOT FOUND' -ForegroundColor Red; exit 1 }

[ZeepCutTrayClick]::SetCursorPos(1700, 20) | Out-Null
[ZeepCutTrayClick]::PostMessage($tray, [ZeepCutTrayClick]::WM_TRAYICON, [IntPtr]::Zero, [IntPtr][int][ZeepCutTrayClick]::WM_RBUTTONUP) | Out-Null

$menu = [IntPtr]::Zero
for ($i = 0; $i -lt 25; $i++) {
    Start-Sleep -Milliseconds 120
    $menu = [ZeepCutTrayClick]::FindPopupMenu()
    if ($menu -ne [IntPtr]::Zero) { break }
}
if ($menu -eq [IntPtr]::Zero) { Write-Host 'POPUP MENU DID NOT OPEN' -ForegroundColor Red; exit 2 }
Start-Sleep -Milliseconds 400

$r = New-Object ZeepCutTrayClick+RECT
[ZeepCutTrayClick]::GetWindowRect($menu, [ref]$r) | Out-Null
$w = $r.Right - $r.Left
$h = $r.Bottom - $r.Top
Write-Host ("POPUP MENU {0},{1} {2}x{3}" -f $r.Left, $r.Top, $w, $h)

if ($Shot) {
    $bmpShot = New-Object System.Drawing.Bitmap($w, $h)
    $gShot = [System.Drawing.Graphics]::FromImage($bmpShot)
    $gShot.CopyFromScreen($r.Left, $r.Top, 0, 0, $bmpShot.Size)
    $gShot.Dispose()
    $bmpShot.Save($Shot, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmpShot.Dispose()
    Write-Host ("SHOT {0}" -f $Shot)
}

# Scan for text rows inside the menu (dark pixels well inside the left margin).
$bmp = New-Object System.Drawing.Bitmap($w, $h)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($r.Left, $r.Top, 0, 0, $bmp.Size)
$g.Dispose()

# Scan for text rows inside the menu. Glyph stems extend below the last text row
# (e.g. "Check for updates" has a descender), so a gap of >= 4 blank rows ends a band.
$bmp = New-Object System.Drawing.Bitmap($w, $h)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($r.Left, $r.Top, 0, 0, $bmp.Size)
$g.Dispose()

$hasInk = New-Object bool[] $h
$right = [Math]::Min(200, $w)
for ($y = 0; $y -lt $h; $y++) {
    $dark = 0
    for ($x = 14; $x -lt $right; $x++) {
        $c = $bmp.GetPixel($x, $y)
        if (($c.R + $c.G + $c.B) -lt 480) { $dark++ }
    }
    $hasInk[$y] = ($dark -ge 1)
}
$bmp.Dispose()

$rows = @()
$i = 0
while ($i -lt $h) {
    if (-not $hasInk[$i]) { $i++; continue }
    $start = $i
    $last = $i
    $gap = 0
    while ($i -lt $h -and $gap -lt 6) {
        if ($hasInk[$i]) { $last = $i; $gap = 0 } else { $gap++ }
        $i++
    }
    if (($last - $start) -ge 4) { $rows += ,@($start, $last) }
}

$labels = @(
    'Capture screen', 'Pin from clipboard', 'Show/Hide all pins', 'History', 'Settings', 'Check for updates', 'Exit',
    '截图', '从剪贴板贴图', '显示/隐藏全部贴图', '历史', '设置', '检查更新', '退出'
)
$index = -1
if ($Text -match '^Exit$|^退出$') {
    # Exit is the last band; click it after the language normalisation below.
}
# Preferred mapping: the EN menu when the app is in English, ZH when Chinese.
# We know the language from the app log so this stays correct in both.
$log = Get-ChildItem (Join-Path $env:LOCALAPPDATA 'ZeepCut\logs\*.log') -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1
$isEnglish = $true
if ($log) {
    $line = Get-Content $log.FullName -Encoding UTF8 | Where-Object { $_ -match '托盘菜单文案' } | Select-Object -Last 1
    if ($line -and $line -match '语言=ZhHans') { $isEnglish = $false }
}
if (-not $isEnglish) { $Text = ($Text -replace '^Check for updates$', '检查更新') -replace '^Settings$', '设置' }

Write-Host ("MENU BANDS = {0}  language={1}  target='{2}'" -f $rows.Count, $(if ($isEnglish) { 'English' } else { 'Chinese' }), $Text)

if ($Text -match '^Exit$|^退出$') { $index = 6 }
elseif ($Text -match 'update|检查更新') { $index = 5 }
elseif ($Text -match '^Settings$|设置') { $index = 4 }
elseif ($Text -match 'History|历史') { $index = 3 }
elseif ($Text -match 'Capture|截图') { $index = 0 }
else { Write-Host 'UNKNOWN TARGET TEXT' -ForegroundColor Red; [ZeepCutTrayClick]::Click(($r.Left + 30), ($r.Top + 4)); exit 3 }

if ($index -ge $rows.Count) { Write-Host 'BAND INDEX OUT OF RANGE' -ForegroundColor Red; [ZeepCutTrayClick]::Click(($r.Left + 30), ($r.Top + 4)); exit 4 }
$y = $r.Top + [int](($rows[$index][0] + $rows[$index][1]) / 2)
$x = $r.Left + 70
[ZeepCutTrayClick]::Click($x, $y)
Write-Host ("CLICKED '{0}' (band {1}) at {2},{3}" -f $Text, $index, $x, $y)

# The click lands on the popup menu, but the mouse ends up hovering wherever the item was.
# If the item was "Capture screen" (band 0) the overlay opens; anything else just closes the
# menu. Nothing else to do here.
exit 0
