# T20/T21/T22 desktop verification driver: global pin hide/show, clipboard pin, history seam.
#
# One process runs a whole scenario so no agent round-trip is needed while the overlay,
# a pin window, the tray menu or a toast is on screen. All coordinates are physical screen
# pixels (PerMonitorV2).
#
# Scenarios:
#   toggle     primary: pin two selections, remember their hwnds+rects, tray menu
#              "隐藏全部贴图 (2)" -> pins disappear (instances kept), tray menu now says
#              "显示全部贴图 (2)" -> pins come back with the SAME hwnd and SAME rect/opacity.
#   clipboard  put a PNG on the clipboard, move the cursor, Alt+S -> a pin appears near the
#              cursor inside the screen; log has the "external clipboard" history line.
#   empty      clear the clipboard, Alt+S -> toast prompt + no pin + app still alive.
#   text       put TEXT on the clipboard, Alt+S -> "no image" prompt, no pin.
#   busy       hold the clipboard open for 8 s in another process, Alt+S -> "busy" prompt.
#   formats    PNG stream format and file-drop format both create a pin.
#   exit       pin two selections then exit through the tray menu; log shows CloseAll(2).
#
# Evidence PNGs are written into -OutDir (copied to artifacts/t20t22 by the caller).
#
# Examples:
#   ... verify-t20t22.ps1 -Scenario toggle
#   ... verify-t20t22.ps1 -Scenario clipboard -CursorX 900 -CursorY 600
param(
    [ValidateSet('primary','secondary')][string]$Target = 'primary',
    [ValidateSet('toggle','clipboard','empty','text','busy','formats','exit','all')][string]$Scenario = 'toggle',
    [string]$OutDir = '',
    [int]$CursorX = [int]::MinValue,
    [int]$CursorY = [int]::MinValue
)
$ErrorActionPreference = 'Stop'

# The agent reads this transcript through WSL pipes; without this the Chinese menu item
# names come back as mojibake and the evidence cannot be checked.
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }

Add-Type -TypeDefinition @'
using System;
using System.Collections.Specialized;
using System.Runtime.InteropServices;
using System.Text;

public static class ZeepCutT20
{
    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }

    [DllImport("user32.dll")]
    public static extern bool SetProcessDpiAwarenessContext(IntPtr value);

    [DllImport("user32.dll")]
    public static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    public static extern bool GetCursorPos(out POINT point);

    [DllImport("user32.dll")]
    public static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);

    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetClassName(IntPtr hWnd, StringBuilder text, int maxCount);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr FindWindow(string className, string windowName);

    [DllImport("user32.dll")]
    public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extraInfo);

    public const uint LEFTDOWN = 0x0002;
    public const uint LEFTUP = 0x0004;
    public const uint RIGHTDOWN = 0x0008;
    public const uint RIGHTUP = 0x0010;
    public const uint MOUSEEVENTF_WHEEL = 0x0800;

    // Negative wheel deltas must be passed as an unchecked uint; PowerShell cannot convert
    // a negative Int32 into the UInt32 dwData parameter, so wrap it here.
    public static void Wheel(int delta)
    {
        mouse_event(MOUSEEVENTF_WHEEL, 0, 0, unchecked((uint)delta), UIntPtr.Zero);
    }

    public static string Title(IntPtr hwnd)
    {
        var t = new StringBuilder(256);
        GetWindowText(hwnd, t, 256);
        return t.ToString();
    }

    public static string ClassOf(IntPtr hwnd)
    {
        var c = new StringBuilder(256);
        GetClassName(hwnd, c, 256);
        return c.ToString();
    }

    public static string Describe(IntPtr hwnd)
    {
        RECT r;
        if (!GetWindowRect(hwnd, out r)) { return string.Format("rect? title={0}", Title(hwnd)); }
        return string.Format("hwnd=0x{0:X} {1},{2} {3}x{4} title={5}",
            hwnd.ToInt64(), r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top, Title(hwnd));
    }

    public static IntPtr FindTrayWindow()
    {
        return FindWindow("ZeepCut.TrayMessageWindow", null);
    }
}
'@

[ZeepCutT20]::SetProcessDpiAwarenessContext([IntPtr](-4)) | Out-Null
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

if ([string]::IsNullOrWhiteSpace($OutDir)) { $OutDir = Join-Path $env:TEMP 'zeepcut-t20t22' }
New-Item -ItemType Directory -Path $OutDir -Force | Out-Null

$screen = $null
foreach ($s in [System.Windows.Forms.Screen]::AllScreens) {
    if ($Target -eq 'primary' -and $s.Primary) { $screen = $s; break }
    if ($Target -eq 'secondary' -and -not $s.Primary) { $screen = $s; break }
}
if (-not $screen) { Write-Host ("screen not found: {0}" -f $Target) -ForegroundColor Red; exit 1 }
$rect = $screen.Bounds
$isPrimary = [bool]$screen.Primary
if ($CursorX -eq [int]::MinValue) { $CursorX = $rect.Left + [int]($rect.Width / 2) }
if ($CursorY -eq [int]::MinValue) { $CursorY = $rect.Top + [int]($rect.Height / 2) }
Write-Host ("SCREEN {0} origin=({1},{2}) {3}x{4} primary={5}" -f $screen.DeviceName, $rect.Left, $rect.Top, $rect.Width, $rect.Height, $isPrimary)

# Pin selections are placed relative to the screen so the same numbers work on both screens.
if ($isPrimary) { $SelX = 360; $SelY = 320 } else { $SelX = -1000; $SelY = 320 }

function Save-ScreenPng([string]$name) {
    $path = Join-Path $OutDir $name
    $bmp = New-Object System.Drawing.Bitmap($rect.Width, $rect.Height)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($rect.Left, $rect.Top, 0, 0, $bmp.Size)
    $g.Dispose()
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Host ("SAVED {0}" -f $path)
}

function Move-Cursor([int]$x, [int]$y) {
    for ($attempt = 0; $attempt -lt 8; $attempt++) {
        [ZeepCutT20]::SetCursorPos($x, $y) | Out-Null
        Start-Sleep -Milliseconds 80
        $p = New-Object ZeepCutT20+POINT
        [ZeepCutT20]::GetCursorPos([ref]$p) | Out-Null
        if ([Math]::Abs($p.X - $x) -le 1 -and [Math]::Abs($p.Y - $y) -le 1) { return }
        Start-Sleep -Milliseconds 150
    }
    Write-Host ("CURSOR UNREACHABLE {0},{1}" -f $x, $y) -ForegroundColor Red
}

function Invoke-Click([int]$x, [int]$y) {
    Move-Cursor $x $y
    [ZeepCutT20]::mouse_event([ZeepCutT20]::LEFTDOWN, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 90
    [ZeepCutT20]::mouse_event([ZeepCutT20]::LEFTUP, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 400
    Write-Host ("CLICK {0},{1}" -f $x, $y)
}

function Invoke-RightClick([int]$x, [int]$y) {
    Move-Cursor $x $y
    [ZeepCutT20]::mouse_event([ZeepCutT20]::RIGHTDOWN, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 90
    [ZeepCutT20]::mouse_event([ZeepCutT20]::RIGHTUP, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 700
    Write-Host ("RIGHTCLICK {0},{1}" -f $x, $y)
}

function Invoke-Drag([int]$x1, [int]$y1, [int]$x2, [int]$y2, [int]$steps = 18) {
    Move-Cursor $x1 $y1
    [ZeepCutT20]::mouse_event([ZeepCutT20]::LEFTDOWN, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 150
    for ($i = 1; $i -le $steps; $i++) {
        $cx = [int][Math]::Round($x1 + (($x2 - $x1) * $i / $steps))
        $cy = [int][Math]::Round($y1 + (($y2 - $y1) * $i / $steps))
        [ZeepCutT20]::SetCursorPos($cx, $cy) | Out-Null
        Start-Sleep -Milliseconds 25
    }
    Start-Sleep -Milliseconds 150
    [ZeepCutT20]::mouse_event([ZeepCutT20]::LEFTUP, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 400
    Write-Host ("DRAG {0},{1} -> {2},{3}" -f $x1, $y1, $x2, $y2)
}

function Send-Keys([string]$keys) {
    [System.Windows.Forms.SendKeys]::SendWait($keys)
    Start-Sleep -Milliseconds 350
    Write-Host ("SENT {0}" -f $keys)
}

function Get-AppPid {
    return (Get-Process ZeepCut -ErrorAction SilentlyContinue | Select-Object -First 1).Id
}

# Pin windows are titled "Zeep Cut 贴图 <pid>"; hidden pins are Collapsed so IsWindowVisible
# is false, which is exactly what "hidden" means for T20.
function Find-PinWindows {
    $appPid = Get-AppPid
    if (-not $appPid) { Write-Host 'ZeepCut is not running' -ForegroundColor Red; return @() }
    $script:pins = New-Object System.Collections.ArrayList
    [ZeepCutT20]::EnumWindows({ param($h, $p)
        $procId = 0
        [void][ZeepCutT20]::GetWindowThreadProcessId($h, [ref]$procId)
        if ($procId -ne $appPid -or -not [ZeepCutT20]::IsWindowVisible($h)) { return $true }
        if (-not [ZeepCutT20]::Title($h).StartsWith('Zeep Cut 贴图')) { return $true }
        $r = New-Object ZeepCutT20+RECT
        if (-not [ZeepCutT20]::GetWindowRect($h, [ref]$r)) { return $true }
        if (($r.Right - $r.Left) -le 0 -or ($r.Bottom - $r.Top) -le 0) { return $true }
        [void]$script:pins.Add([pscustomobject]@{
            Hwnd = $h
            Title = [ZeepCutT20]::Title($h)
            Left = $r.Left; Top = $r.Top; Width = ($r.Right - $r.Left); Height = ($r.Bottom - $r.Top)
        })
        return $true
    }, [IntPtr]::Zero) | Out-Null
    return $script:pins | Sort-Object -Property @{ Expression = { ($_.Left * 10000) + $_.Top } }
}

# Close every pin by clicking its own × button (Esc would need the pin to be foreground,
# which a background process cannot reliably force on Windows).
function Close-AllPins {
    $pins = @(Find-PinWindows)
    if ($pins.Count -eq 0) { return }
    foreach ($pin in $pins) {
        Invoke-Click ([int]($pin.Left + $pin.Width - 15)) ([int]($pin.Top + 15))
        Start-Sleep -Milliseconds 600
    }
    $left = @(Find-PinWindows)
    Write-Host ("CLOSED ALL PINS: remaining={0}" -f $left.Count)
}

function Get-PinSignature {
    $pins = @(Find-PinWindows)
    return ($pins | ForEach-Object { "0x{0:X}:{1},{2} {3}x{4}" -f ([IntPtr]$_.Hwnd).ToInt64(), $_.Left, $_.Top, $_.Width, $_.Height }) -join ' | '
}

function Report-Pins([string]$label) {
    $pins = Find-PinWindows
    Write-Host ("PINS {0}: visible count={1}" -f $label, @($pins).Count)
    foreach ($pin in @($pins)) { Write-Host ("  " + [ZeepCutT20]::Describe([IntPtr]$pin.Hwnd)) }
    return $pins
}

# Toast windows are titled "Zeep Cut 提示 <pid>" (T20/T21 prompt).
function Find-Toast {
    $appPid = Get-AppPid
    if (-not $appPid) { return $null }
    $script:toast = [IntPtr]::Zero
    [ZeepCutT20]::EnumWindows({ param($h, $p)
        $procId = 0
        [void][ZeepCutT20]::GetWindowThreadProcessId($h, [ref]$procId)
        if ($procId -ne $appPid -or -not [ZeepCutT20]::IsWindowVisible($h)) { return $true }
        if ([ZeepCutT20]::Title($h).StartsWith('Zeep Cut 提示')) { $script:toast = $h }
        return $true
    }, [IntPtr]::Zero) | Out-Null
    return $script:toast
}

function Read-ToastText([IntPtr]$hwnd) {
    try {
        $root = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)
        if (-not $root) { return '' }
        $cond = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::Text)
        $text = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
        if ($text) { return $text.Current.Name }
        return $root.Current.Name
    } catch { return '' }
}

function Wait-ForToast([int]$timeoutMs = 4000) {
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    while ($sw.ElapsedMilliseconds -lt $timeoutMs) {
        $h = Find-Toast
        if ($h -ne [IntPtr]::Zero) {
            Start-Sleep -Milliseconds 250
            return $h
        }
        Start-Sleep -Milliseconds 120
    }
    return [IntPtr]::Zero
}

# --- Tray menu (Win32 #32768 popup) automation ---
# Win32 popup menus expose no UI Automation items, and on Windows 11 the icon lives in the
# "Show Hidden Icons" flyout. scripts/tray-menu.ps1 handles both; this wrapper only reports.
# Menu band order (fixed in TrayIcon.BuildMenu):
#   0 截图 | 1 从剪贴板贴图 | 2 显示或隐藏全部贴图 | 3 历史 | 4 设置 | 5 检查更新 | 6 退出
function Invoke-TrayMenu([int]$index, [string]$shotPath = '') {
    $script = Join-Path $PSScriptRoot 'tray-menu.ps1'
    $args = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $script, '-Index', $index)
    if ($shotPath) { $args += @('-Shot', $shotPath) }
    $out = & powershell.exe @args 2>&1
    foreach ($line in $out) { Write-Host ("TRAY: {0}" -f $line) }
    Start-Sleep -Milliseconds 600
}

$script:logPathForHint = Join-Path $env:LOCALAPPDATA ('ZeepCut\logs\app-' + (Get-Date).ToString('yyyyMMdd') + '.log')

# Log checks MUST run here (WSL), not in the Windows PowerShell process: on this machine the
# Windows shell sees a stale 907-byte view of app-YYYYMMDD.log while the app appends to the
# real file. The agent greps the log from WSL, so the script only prints a pointer.
function Show-LogHint([string]$why) {
    Write-Host ('LOG CHECK (run from WSL): grep -nE "' + $why + '" ' + $script:logPathForHint)
}

# --- overlay toolbar helper (same pattern as verify-t18t19.ps1) ---
function Click-OverlayButton([string]$name) {
    $targetPid = Get-AppPid
    if (-not $targetPid) { return $false }
    $script:overlayHwnd = [IntPtr]::Zero
    [ZeepCutT20]::EnumWindows({ param($h, $p)
        $procId = 0
        [void][ZeepCutT20]::GetWindowThreadProcessId($h, [ref]$procId)
        if ($procId -eq $targetPid -and [ZeepCutT20]::IsWindowVisible($h) -and [ZeepCutT20]::Title($h).StartsWith('Zeep Cut 截图遮罩')) {
            $script:overlayHwnd = $h
        }
        return $true
    }, [IntPtr]::Zero) | Out-Null
    if ($script:overlayHwnd -eq [IntPtr]::Zero) { Write-Host ('UIA: overlay not found for ' + $name) -ForegroundColor Yellow; return $false }
    $root = [System.Windows.Automation.AutomationElement]::FromHandle($script:overlayHwnd)
    $cond = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::Button)),
        (New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::NameProperty,
            $name)))
    $button = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
    if (-not $button) { Write-Host ("UIA BUTTON '{0}' NOT FOUND" -f $name) -ForegroundColor Yellow; return $false }
    $r = $button.Current.BoundingRectangle
    Write-Host ("UIA BUTTON '{0}' {1},{2} {3}x{4}" -f $name, $r.X, $r.Y, $r.Width, $r.Height)
    Invoke-Click ([int]($r.X + $r.Width / 2)) ([int]($r.Y + $r.Height / 2))
    return $true
}

# Pin one selection through the real Alt+A -> drag -> 贴图 path.
function New-Pin([int]$x, [int]$y, [int]$w, [int]$h) {
    Move-Cursor ([int]($rect.Left + $rect.Width / 2)) ([int]($rect.Top + $rect.Height / 2))
    Send-Keys '%a'
    Start-Sleep -Milliseconds 1800
    Invoke-Drag $x $y ($x + $w) ($y + $h)
    if (-not (Click-OverlayButton '贴图')) { Write-Host 'OVERLAY PIN BUTTON MISSING' -ForegroundColor Red; exit 3 }
    Start-Sleep -Milliseconds 1500
    Write-Host ("EXPECT PIN RECT {0},{1} {2}x{3}" -f $x, $y, $w, $h)
}

# --- clipboard fixtures (this script is verification-only, WinForms is allowed here) ---
function New-ClipboardSourcePng([int]$w = 300, [int]$h = 200) {
    $path = Join-Path $OutDir 'clipboard-source.png'
    $bmp = New-Object System.Drawing.Bitmap($w, $h)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.Clear([System.Drawing.Color]::FromArgb(255, 32, 96, 200))
    $brush1 = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 255, 210, 40))
    $brush2 = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 240, 60, 60))
    $g.FillRectangle($brush1, 10, 10, 120, 80)
    $g.FillRectangle($brush2, 160, 100, 120, 80)
    $font = New-Object System.Drawing.Font('Segoe UI', 16)
    $white = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
    $g.DrawString('T21 CLIP', $font, $white, 60, 130)
    $g.Dispose()
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Host ("CLIPBOARD SOURCE PNG {0} {1}x{2}" -f $path, $w, $h)
    return $path
}

function Set-ClipboardImage([string]$path) {
    $bmp = [System.Drawing.Image]::FromFile($path)
    [System.Windows.Forms.Clipboard]::SetImage($bmp)
    Start-Sleep -Milliseconds 300
    $check = [System.Windows.Forms.Clipboard]::GetImage()
    if ($check) { Write-Host ("CLIPBOARD SET IMAGE {0}x{1}" -f $check.Width, $check.Height); $check.Dispose() }
    else { Write-Host 'CLIPBOARD SET IMAGE FAILED' -ForegroundColor Red }
    # Keep $bmp alive on purpose: the clipboard DIB may reference it until the app reads it.
    $script:clipboardBitmap = $bmp
}

function Set-ClipboardPngStream([string]$path) {
    $bytes = [System.IO.File]::ReadAllBytes($path)
    $stream = New-Object System.IO.MemoryStream(,$bytes)
    [System.Windows.Forms.Clipboard]::SetData('PNG', $stream)
    Start-Sleep -Milliseconds 300
    $formats = [System.Windows.Forms.Clipboard]::GetDataObject().GetFormats($false)
    Write-Host ("CLIPBOARD PNG STREAM formats=" + ($formats -join ','))
}

function Set-ClipboardFileDrop([string]$path) {
    $col = New-Object System.Collections.Specialized.StringCollection
    [void]$col.Add($path)
    [System.Windows.Forms.Clipboard]::SetFileDropList($col)
    Start-Sleep -Milliseconds 300
    $formats = [System.Windows.Forms.Clipboard]::GetDataObject().GetFormats($false)
    Write-Host ("CLIPBOARD FILEDROP formats=" + ($formats -join ','))
}

function Clear-Clipboard {
    [System.Windows.Forms.Clipboard]::Clear()
    Start-Sleep -Milliseconds 250
    Write-Host 'CLIPBOARD CLEARED'
}

# Alt+S at a chosen cursor position, then wait for the pin count to change.
function Invoke-ClipboardPin([int]$x, [int]$y, [int]$expectedCount, [int]$timeoutMs = 5000) {
    Move-Cursor $x $y
    Send-Keys '%s'
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    while ($sw.ElapsedMilliseconds -lt $timeoutMs) {
        if (@(Find-PinWindows).Count -eq $expectedCount) { Start-Sleep -Milliseconds 400; return $true }
        Start-Sleep -Milliseconds 200
    }
    return $false
}

$before = Report-Pins 'before'
if (@($before).Count -ne 0) {
    Write-Host 'existing pins found; closing them first' -ForegroundColor Yellow
    Close-AllPins
    Report-Pins 'after-cleanup' | Out-Null
}

switch ($Scenario) {
    'toggle' {
        # ---- two pins ----
        New-Pin $SelX $SelY 300 220
        New-Pin ($SelX + 340) ($SelY + 40) 340 260
        $pins = @(Report-Pins 'toggle-after-create')
        Save-ScreenPng ("{0}-toggle-01-two-pins.png" -f $Target)
        $visibleSignature = Get-PinSignature
        Write-Host ("TOGGLE VISIBLE SIGNATURE: {0}" -f $visibleSignature)
        Write-Host ("EXPECT 2 PINS: {0}" -f ($pins.Count -eq 2))

        # hide one of them briefly so the "restore opacity/scale" claim is non-trivial:
        # zoom the second pin and fade it before hiding everything.
        $second = [IntPtr]$pins[1].Hwnd
        $r = New-Object ZeepCutT20+RECT
        [ZeepCutT20]::GetWindowRect($second, [ref]$r) | Out-Null
        $ax = $r.Left + 40; $ay = $r.Top + 40
        Move-Cursor $ax $ay
        [ZeepCutT20]::Wheel(120)
        Start-Sleep -Milliseconds 150
        [ZeepCutT20]::Wheel(120)
        Start-Sleep -Milliseconds 300
        [ZeepCutT20]::keybd_event(0x11, 0, 0, [UIntPtr]::Zero)
        Start-Sleep -Milliseconds 100
        for ($i = 0; $i -lt 3; $i++) {
            [ZeepCutT20]::Wheel(-120)
            Start-Sleep -Milliseconds 120
        }
        [ZeepCutT20]::keybd_event(0x11, 0, 2, [UIntPtr]::Zero)
        Start-Sleep -Milliseconds 500
        $pins = @(Report-Pins 'toggle-after-zoom-fade')
        Save-ScreenPng ("{0}-toggle-02-zoomed-faded.png" -f $Target)
        $visibleSignature = Get-PinSignature
        Write-Host ("TOGGLE VISIBLE SIGNATURE (zoomed+faded): {0}" -f $visibleSignature)

        # ---- hide all through the tray menu ----
        Invoke-TrayMenu 2 ("{0}-toggle-02b-tray-menu-hide.png" -f $Target)
        Start-Sleep -Milliseconds 400
        Save-ScreenPng ("{0}-toggle-03-hidden-toast.png" -f $Target)
        $hidden = @(Report-Pins 'toggle-after-hide')
        Write-Host ("EXPECT 0 VISIBLE PINS AFTER HIDE: {0}" -f ($hidden.Count -eq 0))
        Start-Sleep -Milliseconds 2500
        Save-ScreenPng ("{0}-toggle-04-hidden.png" -f $Target)

        # ---- show all again: same hwnd, same rect ----
        Invoke-TrayMenu 2 ("{0}-toggle-04b-tray-menu-show.png" -f $Target)
        Start-Sleep -Milliseconds 600
        $afterShow = @(Report-Pins 'toggle-after-show')
        Save-ScreenPng ("{0}-toggle-05-shown.png" -f $Target)
        $restoredSignature = Get-PinSignature
        Write-Host ("TOGGLE RESTORED SIGNATURE: {0}" -f $restoredSignature)
        Write-Host ("RESTORED IDENTICAL (hwnd+position+size): {0}" -f ($restoredSignature -eq $visibleSignature))
        Write-Host ("EXPECT 2 VISIBLE PINS AFTER SHOW: {0}" -f ($afterShow.Count -eq 2))
        Start-Sleep -Milliseconds 2500
        Save-ScreenPng ("{0}-toggle-06-shown-clean.png" -f $Target)

        Show-LogHint '隐藏全部贴图|显示全部贴图|已隐藏贴图|已显示贴图|全局显隐'

        # clean up: click each pin's × button
        Close-AllPins
        $final = @(Report-Pins 'toggle-final')
        Write-Host ("EXPECT 0 PINS AT END: {0}" -f ($final.Count -eq 0))
    }
    'clipboard' {
        $source = New-ClipboardSourcePng
        Set-ClipboardImage $source
        $ok = Invoke-ClipboardPin $CursorX $CursorY 1
        Write-Host ("EXPECT 1 PIN AFTER ALT+S: {0}" -f $ok)
        $pins = @(Report-Pins 'clipboard-after')
        Save-ScreenPng ("{0}-clipboard-01-created.png" -f $Target)
        if ($pins.Count -ge 1) {
            $p = $pins[0]
            $inside = ($p.Left -ge $rect.Left) -and ($p.Top -ge $rect.Top) -and
                      (($p.Left + $p.Width) -le ($rect.Left + $rect.Width)) -and
                      (($p.Top + $p.Height) -le ($rect.Top + $rect.Height))
            $dx = [Math]::Abs($p.Left - $CursorX)
            $dy = [Math]::Abs($p.Top - $CursorY)
            Write-Host ("CLIPBOARD PIN {0},{1} {2}x{3} cursor={4},{5} inside={6} |dx|={7} |dy|={8}" -f
                $p.Left, $p.Top, $p.Width, $p.Height, $CursorX, $CursorY, $inside, $dx, $dy)
            Write-Host ("EXPECT IMAGE 300x200: {0}" -f (($p.Width -eq 300) -and ($p.Height -eq 200)))
            Write-Host 'EXPECT PIN INSIDE SCREEN AND NEAR CURSOR'
        }
        Show-LogHint '从剪贴板贴图|剪贴板图片读取成功|剪贴板贴图完成|贴图历史联动'

        # close it so the next scenario starts clean
        Close-AllPins
        $final = @(Report-Pins 'clipboard-final')
        Write-Host ("EXPECT 0 PINS AT END: {0}" -f ($final.Count -eq 0))
    }
    'empty' {
        Clear-Clipboard
        Move-Cursor $CursorX $CursorY
        Send-Keys '%s'
        $toast = Wait-ForToast
        if ($toast -ne [IntPtr]::Zero) {
            Write-Host ("TOAST " + [ZeepCutT20]::Describe($toast))
            Write-Host ("TOAST TEXT [{0}]" -f (Read-ToastText $toast))
        } else {
            Write-Host 'TOAST NOT FOUND (expected a prompt for the empty clipboard)' -ForegroundColor Yellow
        }
        Save-ScreenPng ("{0}-empty-01-toast.png" -f $Target)
        Start-Sleep -Milliseconds 500
        $pins = @(Report-Pins 'empty-after')
        Write-Host ("EXPECT 0 PINS: {0}" -f ($pins.Count -eq 0))
        $proc = Get-Process ZeepCut -ErrorAction SilentlyContinue
        Write-Host ("EXPECT APP STILL RUNNING: {0}" -f ($null -ne $proc))
        Show-LogHint '剪贴板贴图失败|剪贴板是空的'
        Start-Sleep -Milliseconds 2200
        Save-ScreenPng ("{0}-empty-02-toast-gone.png" -f $Target)
    }
    'text' {
        [System.Windows.Forms.Clipboard]::SetText('Zeep Cut T21：这里只有文字，没有图片')
        Start-Sleep -Milliseconds 300
        Write-Host 'CLIPBOARD SET TEXT'
        Move-Cursor $CursorX $CursorY
        Send-Keys '%s'
        $toast = Wait-ForToast
        if ($toast -ne [IntPtr]::Zero) {
            Write-Host ("TOAST " + [ZeepCutT20]::Describe($toast))
            Write-Host ("TOAST TEXT [{0}]" -f (Read-ToastText $toast))
        } else {
            Write-Host 'TOAST NOT FOUND (expected a "no image" prompt)' -ForegroundColor Yellow
        }
        Save-ScreenPng ("{0}-text-01-toast.png" -f $Target)
        $pins = @(Report-Pins 'text-after')
        Write-Host ("EXPECT 0 PINS: {0}" -f ($pins.Count -eq 0))
        Show-LogHint '剪贴板贴图失败|没有图片'
        Start-Sleep -Milliseconds 2500
    }
    'busy' {
        $holder = Start-Process -FilePath 'powershell.exe' -PassThru -ArgumentList @(
            '-NoProfile', '-ExecutionPolicy', 'Bypass',
            '-File', (Join-Path $PSScriptRoot 'clipboard.ps1'), '-HoldSeconds', '10')
        Start-Sleep -Milliseconds 1200
        Write-Host ("CLIPBOARD HELD BY pid {0}" -f $holder.Id)
        Move-Cursor $CursorX $CursorY
        Send-Keys '%s'
        $toast = Wait-ForToast 6000
        if ($toast -ne [IntPtr]::Zero) {
            Write-Host ("TOAST " + [ZeepCutT20]::Describe($toast))
            Write-Host ("TOAST TEXT [{0}]" -f (Read-ToastText $toast))
        } else {
            Write-Host 'TOAST NOT FOUND (expected a "clipboard busy" prompt)' -ForegroundColor Yellow
        }
        Save-ScreenPng ("{0}-busy-01-toast.png" -f $Target)
        $pins = @(Report-Pins 'busy-after')
        Write-Host ("EXPECT 0 PINS: {0}" -f ($pins.Count -eq 0))
        $proc = Get-Process ZeepCut -ErrorAction SilentlyContinue
        Write-Host ("EXPECT APP STILL RUNNING: {0}" -f ($null -ne $proc))
        Show-LogHint '读取剪贴板失败|剪贴板贴图失败'
        Start-Sleep -Milliseconds 1200
        if (-not $holder.HasExited) { Stop-Process -Id $holder.Id -Force -ErrorAction SilentlyContinue }
        Start-Sleep -Milliseconds 500
        Write-Host 'CLIPBOARD HOLDER STOPPED'
    }
    'formats' {
        $source = New-ClipboardSourcePng 240 160

        # 1) PNG stream format
        Set-ClipboardPngStream $source
        $okPng = Invoke-ClipboardPin $CursorX $CursorY 1
        Write-Host ("EXPECT 1 PIN FROM PNG STREAM: {0}" -f $okPng)
        $pins = @(Report-Pins 'formats-png')
        Save-ScreenPng ("{0}-formats-01-png.png" -f $Target)
        if ($pins.Count -ge 1) { Write-Host ("PNG PIN {0}x{1}" -f $pins[0].Width, $pins[0].Height) }
        Close-AllPins

        # 2) file drop format (as if copied from Explorer)
        Set-ClipboardFileDrop $source
        $okDrop = Invoke-ClipboardPin ($CursorX + 120) ($CursorY + 80) 1
        Write-Host ("EXPECT 1 PIN FROM FILE DROP: {0}" -f $okDrop)
        $pins = @(Report-Pins 'formats-filedrop')
        Save-ScreenPng ("{0}-formats-02-filedrop.png" -f $Target)
        if ($pins.Count -ge 1) { Write-Host ("FILEDROP PIN {0}x{1}" -f $pins[0].Width, $pins[0].Height) }
        Show-LogHint '剪贴板图片读取成功'
        Close-AllPins
        $final = @(Report-Pins 'formats-final')
        Write-Host ("EXPECT 0 PINS AT END: {0}" -f ($final.Count -eq 0))
    }
    'exit' {
        New-Pin $SelX $SelY 300 220
        New-Pin ($SelX + 340) ($SelY + 40) 340 260
        $pins = @(Report-Pins 'exit-before')
        Save-ScreenPng ("{0}-exit-01-two-pins.png" -f $Target)
        Write-Host ("EXPECT 2 PINS: {0}" -f ($pins.Count -eq 2))
        Invoke-TrayMenu 6 ("{0}-exit-01b-tray-menu.png" -f $Target)
        Start-Sleep -Milliseconds 2000
        $proc = Get-Process ZeepCut -ErrorAction SilentlyContinue
        Write-Host ("EXPECT APP EXITED: {0}" -f ($null -eq $proc))
        Save-ScreenPng ("{0}-exit-02-after-exit.png" -f $Target)
        Show-LogHint '退出：关闭全部贴图|贴图窗口已关闭|退出完成'
    }
    'all' {
        Write-Host 'Use one scenario per invocation (toggle/clipboard/empty/text/busy/formats/exit).'
    }
}
