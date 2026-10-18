# T18/T19 desktop verification driver: pin windows (basic + enhanced).
#
# One process runs a whole scenario so no agent round-trip is needed while the overlay
# or a pin window is on screen. All coordinates are physical screen pixels (PerMonitorV2).
#
# Scenarios:
#   basic     primary screen: pin one 640x420 selection -> verify window rect, drag it,
#             wheel-zoom (2 steps out, 2 steps in), Ctrl+wheel fade to the 20% floor,
#             right-click menu items (原始大小 / 复制 / 关闭), Esc close.
#   lock      primary: pin one selection, right-click 锁定位置, try to drag (must not move),
#             wheel still zooms, right-click 解锁位置, drag works again.
#   menu      primary: pin one selection, right-click 原始大小 after zooming (size restored),
#             复制 (clipboard image read by a separate process), 另存为 PNG (real dialog).
#   multi     primary: two pins side by side, drag one and verify the other did not move,
#             close one with its × button, the other stays.
#   keep      create two pins and leave them open (used to test the app exit path).
#   limits    wheel down past the minimum (40 px) and up past the maximum (8x).
#   secondary pin on the secondary screen (-1080,0, 100%) and verify the physical rect.
#
# Evidence is written as PNGs into -OutDir (copied to artifacts/t18t19 by the caller).
#
# Examples:
#   ... verify-t18t19.ps1 -Scenario basic
#   ... verify-t18t19.ps1 -Scenario multi
#   ... verify-t18t19.ps1 -Target secondary -Scenario basic
param(
    [ValidateSet('primary','secondary')][string]$Target = 'primary',
    [ValidateSet('basic','lock','menu','multi','keep','limits','crossscreen')][string]$Scenario = 'basic',
    [string]$OutDir = '',
    [int]$SelX = [int]::MinValue,
    [int]$SelY = [int]::MinValue,
    [int]$SelW = 640,
    [int]$SelH = 420
)
$ErrorActionPreference = 'Stop'

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Text;

public static class ZeepCutT18
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

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetClassName(IntPtr hWnd, StringBuilder text, int maxCount);

    public delegate bool EnumChildProc(IntPtr parent, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool EnumChildWindows(IntPtr parent, EnumChildProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern int GetDlgCtrlID(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    public const uint BM_CLICK = 0x00F5;

    public const uint LEFTDOWN = 0x0002;
    public const uint LEFTUP = 0x0004;
    public const uint RIGHTDOWN = 0x0008;
    public const uint RIGHTUP = 0x0010;
    public const uint MOUSEEVENTF_WHEEL = 0x0800;

    public static void Wheel(int delta)
    {
        mouse_event(MOUSEEVENTF_WHEEL, 0, 0, unchecked((uint)delta), UIntPtr.Zero);
    }

    [DllImport("user32.dll")]
    public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    public static string Describe(IntPtr hwnd)
    {
        var t = new StringBuilder(256);
        GetWindowText(hwnd, t, 256);
        var cls = new StringBuilder(256);
        GetClassName(hwnd, cls, 256);
        var ex = GetWindowLong(hwnd, -20);
        var tool = ((ex & 0x80) != 0) ? "TOOLWINDOW" : "no-toolwindow";
        RECT r;
        if (!GetWindowRect(hwnd, out r)) { return string.Format("rect? class={0} title={1}", cls, t); }
        return string.Format("{0},{1} {2}x{3} class={4} exstyle=0x{5:X8}({6}) title={7}", r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top, cls, ex, tool, t);
    }
}
'@

[ZeepCutT18]::SetProcessDpiAwarenessContext([IntPtr](-4)) | Out-Null
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

if ([string]::IsNullOrWhiteSpace($OutDir)) { $OutDir = Join-Path $env:TEMP 'zeepcut-t18t19' }
New-Item -ItemType Directory -Path $OutDir -Force | Out-Null

$screen = $null
foreach ($s in [System.Windows.Forms.Screen]::AllScreens) {
    if ($Target -eq 'primary' -and $s.Primary) { $screen = $s; break }
    if ($Target -eq 'secondary' -and -not $s.Primary) { $screen = $s; break }
}
if (-not $screen) { Write-Host ("screen not found: {0}" -f $Target) -ForegroundColor Red; exit 1 }
$rect = $screen.Bounds
$isPrimary = [bool]$screen.Primary
Write-Host ("SCREEN {0} origin=({1},{2}) {3}x{4} primary={5}" -f $screen.DeviceName, $rect.Left, $rect.Top, $rect.Width, $rect.Height, $isPrimary)

if ($SelX -eq [int]::MinValue) { if ($isPrimary) { $SelX = 360 } else { $SelX = -1000 } }
if ($SelY -eq [int]::MinValue) { $SelY = 320 }

# Saves a screenshot of ANY screen (used by crossscreen, where the pin moves off $rect).
function Save-OtherScreenPng([string]$name, [bool]$primary) {
    $s2 = $null
    foreach ($x in [System.Windows.Forms.Screen]::AllScreens) {
        if ([bool]$x.Primary -eq $primary) { $s2 = $x; break }
    }
    if (-not $s2) { Write-Host 'other screen missing' -ForegroundColor Red; return }
    $b = $s2.Bounds
    $path = Join-Path $OutDir $name
    $bmp = New-Object System.Drawing.Bitmap($b.Width, $b.Height)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($b.Left, $b.Top, 0, 0, $bmp.Size)
    $g.Dispose()
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Host ("SAVED {0} (screen origin {1},{2} {3}x{4})" -f $path, $b.Left, $b.Top, $b.Width, $b.Height)
}

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
        [ZeepCutT18]::SetCursorPos($x, $y) | Out-Null
        Start-Sleep -Milliseconds 80
        $p = New-Object ZeepCutT18+POINT
        [ZeepCutT18]::GetCursorPos([ref]$p) | Out-Null
        if ([Math]::Abs($p.X - $x) -le 1 -and [Math]::Abs($p.Y - $y) -le 1) { return }
        Start-Sleep -Milliseconds 150
    }
    Write-Host ("CURSOR UNREACHABLE {0},{1}" -f $x, $y) -ForegroundColor Red
}

function Invoke-Click([int]$x, [int]$y) {
    Move-Cursor $x $y
    [ZeepCutT18]::mouse_event([ZeepCutT18]::LEFTDOWN, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 90
    [ZeepCutT18]::mouse_event([ZeepCutT18]::LEFTUP, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 400
    Write-Host ("CLICK {0},{1}" -f $x, $y)
}

function Invoke-Drag([int]$x1, [int]$y1, [int]$x2, [int]$y2, [int]$steps = 18) {
    Move-Cursor $x1 $y1
    [ZeepCutT18]::mouse_event([ZeepCutT18]::LEFTDOWN, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 150
    for ($i = 1; $i -le $steps; $i++) {
        $cx = [int][Math]::Round($x1 + (($x2 - $x1) * $i / $steps))
        $cy = [int][Math]::Round($y1 + (($y2 - $y1) * $i / $steps))
        [ZeepCutT18]::SetCursorPos($cx, $cy) | Out-Null
        Start-Sleep -Milliseconds 25
    }
    Start-Sleep -Milliseconds 150
    [ZeepCutT18]::mouse_event([ZeepCutT18]::LEFTUP, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 400
    Write-Host ("DRAG {0},{1} -> {2},{3}" -f $x1, $y1, $x2, $y2)
}

function Invoke-Wheel([int]$x, [int]$y, [int]$notches, [switch]$Ctrl) {
    Move-Cursor $x $y
    if ($Ctrl) { Set-Ctrl $true }
    for ($i = 0; $i -lt [Math]::Abs($notches); $i++) {
        $delta = 120
        if ($notches -lt 0) { $delta = -120 }
        [ZeepCutT18]::Wheel($delta)
        Start-Sleep -Milliseconds 120
    }
    if ($Ctrl) { Set-Ctrl $false }
    Start-Sleep -Milliseconds 350
    Write-Host ("WHEEL x{0} ctrl={1} at {2},{3}" -f $notches, [bool]$Ctrl, $x, $y)
}

# Hold/release the physical Ctrl key while wheeling (SendKeys '^' does not hold it).
function Set-Ctrl([bool]$down) {
    if ($down) { [ZeepCutT18]::keybd_event(0x11, 0, 0, [UIntPtr]::Zero) }
    else { [ZeepCutT18]::keybd_event(0x11, 0, 2, [UIntPtr]::Zero) }
    Start-Sleep -Milliseconds 120
}

function Send-Keys([string]$keys) {
    [System.Windows.Forms.SendKeys]::SendWait($keys)
    Start-Sleep -Milliseconds 350
    Write-Host ("SENT {0}" -f $keys)
}

function Get-AppPid {
    return (Get-Process ZeepCut -ErrorAction SilentlyContinue | Select-Object -First 1).Id
}

# Pin windows are titled "Zeep Cut 贴图 <pid>" so they never collide with the overlay title.
function Find-PinWindows {
    $appPid = Get-AppPid
    if (-not $appPid) { Write-Host 'ZeepCut is not running' -ForegroundColor Red; return @() }
    $script:pins = New-Object System.Collections.ArrayList
    [ZeepCutT18]::EnumWindows({ param($h, $p)
        $procId = 0
        [void][ZeepCutT18]::GetWindowThreadProcessId($h, [ref]$procId)
        if ($procId -ne $appPid -or -not [ZeepCutT18]::IsWindowVisible($h)) { return $true }
        $sb = New-Object System.Text.StringBuilder 256
        [void][ZeepCutT18]::GetWindowText($h, $sb, 256)
        if (-not $sb.ToString().StartsWith('Zeep Cut 贴图')) { return $true }
        $r = New-Object ZeepCutT18+RECT
        if (-not [ZeepCutT18]::GetWindowRect($h, [ref]$r)) { return $true }
        if (($r.Right - $r.Left) -le 0 -or ($r.Bottom - $r.Top) -le 0) { return $true }
        [void]$script:pins.Add([pscustomobject]@{ Hwnd = $h; Title = $sb.ToString(); Left = $r.Left; Top = $r.Top })
        return $true
    }, [IntPtr]::Zero) | Out-Null

    # EnumWindows returns Z order (foreground first), so two pins would swap places.
    # Sort by screen position so $pins[0] is always the left-most/top-most window.
    # Return the plain array: PowerShell unrolls it, and every caller wraps the result in
    # @(...) so a single pin still indexes as $pins[0]. (Do NOT use `return ,$array` here —
    # that nests one array inside another and breaks .Count/indexing.)
    return $script:pins | Sort-Object -Property @{ Expression = { ($_.Left * 10000) + $_.Top } }
}

function Report-Pins([string]$label) {
    $pins = Find-PinWindows
    Write-Host ("PINS {0}: count={1}" -f $label, @($pins).Count)
    foreach ($pin in @($pins)) {
        Write-Host ("  hwnd=0x{0:X} {1}" -f ([IntPtr]$pin.Hwnd).ToInt64(), [ZeepCutT18]::Describe([IntPtr]$pin.Hwnd))
    }
    return $pins
}

function Get-PinRect([IntPtr]$hwnd) {
    $r = New-Object ZeepCutT18+RECT
    [ZeepCutT18]::GetWindowRect($hwnd, [ref]$r) | Out-Null
    return ("{0},{1} {2}x{3}" -f $r.Left, $r.Top, ($r.Right - $r.Left), ($r.Bottom - $r.Top))
}

# Reads the clipboard image from a SEPARATE process (never share clipboard state with the app).
function Save-ClipboardPng([string]$name) {
    $path = Join-Path $OutDir $name
    $script = Join-Path $PSScriptRoot 'clipboard.ps1'
    $out = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $script -Save $path 2>&1
    $out | ForEach-Object { Write-Host ("CLIP: {0}" -f $_) }
}

# UIA helpers for the overlay toolbar (same pattern as verify-t16t17.ps1).
function Get-OverlayRoot {
    $targetPid = Get-AppPid
    if (-not $targetPid) { return $null }
    $script:overlayHwnd = [IntPtr]::Zero
    [ZeepCutT18]::EnumWindows({ param($h, $p)
        $sb = New-Object System.Text.StringBuilder 256
        [void][ZeepCutT18]::GetWindowText($h, $sb, 256)
        $procId = 0
        [void][ZeepCutT18]::GetWindowThreadProcessId($h, [ref]$procId)
        if ($procId -eq $targetPid -and [ZeepCutT18]::IsWindowVisible($h) -and $sb.ToString().StartsWith('Zeep Cut 截图遮罩')) {
            $script:overlayHwnd = $h
        }
        return $true
    }, [IntPtr]::Zero) | Out-Null
    if ($script:overlayHwnd -eq [IntPtr]::Zero) { Write-Host 'UIA: overlay not found' -ForegroundColor Yellow; return $null }
    return [System.Windows.Automation.AutomationElement]::FromHandle($script:overlayHwnd)
}

function Click-Button([string]$name) {
    $root = Get-OverlayRoot
    if (-not $root) { return $false }
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

# Right-click menu items are real WPF MenuItems: find them through UIA in the pin window.
function Click-PinMenuItem([string]$label, [string]$header) {
    $pins = Find-PinWindows
    if (@($pins).Count -eq 0) { Write-Host 'no pin window for menu' -ForegroundColor Red; return $false }
    $hwnd = [IntPtr]$pins[0].Hwnd
    [ZeepCutT18]::SetForegroundWindow($hwnd) | Out-Null
    Start-Sleep -Milliseconds 200

    # Right click in the middle of the pin to open its context menu.
    $r = New-Object ZeepCutT18+RECT
    [ZeepCutT18]::GetWindowRect($hwnd, [ref]$r) | Out-Null
    $cx = [int](($r.Left + $r.Right) / 2)
    $cy = [int](($r.Top + $r.Bottom) / 2)
    Move-Cursor $cx $cy
    [ZeepCutT18]::mouse_event([ZeepCutT18]::RIGHTDOWN, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 80
    [ZeepCutT18]::mouse_event([ZeepCutT18]::RIGHTUP, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 700

    $root = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)
    if (-not $root) { Write-Host 'pin UIA root null' -ForegroundColor Yellow; return $false }
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty,
        $header)
    $item = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
    if (-not $item) {
        Write-Host ("MENU ITEM '{0}' NOT FOUND ({1})" -f $header, $label) -ForegroundColor Yellow
        Send-Keys '{ESC}' | Out-Null
        return $false
    }
    $ir = $item.Current.BoundingRectangle
    Write-Host ("MENU ITEM '{0}' {1},{2} {3}x{4}" -f $header, $ir.X, $ir.Y, $ir.Width, $ir.Height)
    Invoke-Click ([int]($ir.X + $ir.Width / 2)) ([int]($ir.Y + $ir.Height / 2))
    return $true
}

function Wait-ForPinCount([int]$expected, [int]$timeoutMs = 6000) {
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    while ($sw.ElapsedMilliseconds -lt $timeoutMs) {
        if (@(Find-PinWindows).Count -eq $expected) { return $true }
        Start-Sleep -Milliseconds 200
    }
    return $false
}

# --- SaveFileDialog automation (same approach as verify-t16t17.ps1) ---
function Find-SaveDialogHwnd {
    $appPid = Get-AppPid
    if (-not $appPid) { return [IntPtr]::Zero }
    $script:dlgHwnd = [IntPtr]::Zero
    [ZeepCutT18]::EnumWindows({ param($h, $p)
        $procId = 0
        [void][ZeepCutT18]::GetWindowThreadProcessId($h, [ref]$procId)
        if ($procId -ne $appPid) { return $true }
        if (-not [ZeepCutT18]::IsWindowVisible($h)) { return $true }
        $cls = New-Object System.Text.StringBuilder 256
        [void][ZeepCutT18]::GetClassName($h, $cls, 256)
        if ($cls.ToString() -eq '#32770') { $script:dlgHwnd = $h }
        return $true
    }, [IntPtr]::Zero) | Out-Null
    return $script:dlgHwnd
}

function Wait-SaveDialogHwnd([int]$timeoutMs = 8000) {
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    while ($sw.ElapsedMilliseconds -lt $timeoutMs) {
        $h = Find-SaveDialogHwnd
        if ($h -ne [IntPtr]::Zero) { Start-Sleep -Milliseconds 500; return $h }
        Start-Sleep -Milliseconds 200
    }
    return [IntPtr]::Zero
}

function Find-DialogButton([int]$controlId) {
    $hwnd = Wait-SaveDialogHwnd
    if ($hwnd -eq [IntPtr]::Zero) { return [IntPtr]::Zero }
    $script:dlgButton = [IntPtr]::Zero
    [ZeepCutT18]::EnumChildWindows($hwnd, { param($h, $p)
        if ([ZeepCutT18]::GetDlgCtrlID($h) -ne $controlId) { return $true }
        $cls = New-Object System.Text.StringBuilder 256
        [void][ZeepCutT18]::GetClassName($h, $cls, 256)
        if ($cls.ToString() -eq 'Button') { $script:dlgButton = $h }
        return $true
    }, [IntPtr]::Zero) | Out-Null
    return $script:dlgButton
}

# The Win11 dialog file-name field is DirectUI (no ValuePattern): click it and paste
# the path from the clipboard, because the Chinese IME rewrites "C:\..." into "C：、...".
function Set-DialogFileName([string]$path) {
    $hwnd = Wait-SaveDialogHwnd
    if ($hwnd -eq [IntPtr]::Zero) { Write-Host 'DIALOG NOT FOUND' -ForegroundColor Red; return $false }
    $rootDlg = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)
    if (-not $rootDlg) { Write-Host 'DIALOG UIA ROOT NULL' -ForegroundColor Red; return $false }
    $fieldCond = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::Pane)),
        (New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::AutomationIdProperty, '1001')),
        (New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ClassNameProperty, 'Edit')))
    $field = $rootDlg.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $fieldCond)
    if (-not $field) { Write-Host 'DIALOG FILE FIELD NOT FOUND' -ForegroundColor Red; return $false }
    $r = $field.Current.BoundingRectangle
    Write-Host ("DIALOG FILE FIELD {0},{1} {2}x{3} current=[{4}]" -f $r.X, $r.Y, $r.Width, $r.Height, $field.Current.Name)
    Invoke-Click ([int]($r.X + $r.Width / 2)) ([int]($r.Y + $r.Height / 2))
    [System.Windows.Forms.Clipboard]::SetText($path)
    Start-Sleep -Milliseconds 200
    Send-Keys '^a'
    Send-Keys '^v'
    Start-Sleep -Milliseconds 400
    $field = $rootDlg.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $fieldCond)
    if ($field) { Write-Host ("DIALOG FILE NOW [{0}]" -f $field.Current.Name); return ($field.Current.Name -eq $path) }
    return $false
}

function Invoke-DialogSave {
    $button = Find-DialogButton 1
    if ($button -eq [IntPtr]::Zero) { Write-Host 'DIALOG SAVE BUTTON NOT FOUND' -ForegroundColor Red; return $false }
    [ZeepCutT18]::SendMessage($button, [ZeepCutT18]::BM_CLICK, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
    Write-Host 'DIALOG SAVE CLICKED'
    return $true
}

function Wait-ForFile([string]$path, [int]$timeoutMs = 8000) {
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    while ($sw.ElapsedMilliseconds -lt $timeoutMs) {
        if (Test-Path -LiteralPath $path) { return $true }
        Start-Sleep -Milliseconds 200
    }
    return $false
}

# --- Pin one selection through the real Alt+A -> drag -> 贴图 path ---
function New-Pin([int]$x, [int]$y, [int]$w, [int]$h) {
    Move-Cursor ([int]($rect.Left + $rect.Width / 2)) ([int]($rect.Top + $rect.Height / 2))
    Send-Keys '%a'
    Start-Sleep -Milliseconds 1800
    Invoke-Drag $x $y ($x + $w) ($y + $h)
    if (-not (Click-Button '贴图')) { Write-Host 'PIN BUTTON MISSING' -ForegroundColor Red; exit 3 }
    Start-Sleep -Milliseconds 1500
    Write-Host ("EXPECT PIN RECT {0},{1} {2}x{3}" -f $x, $y, $w, $h)
}

$before = Report-Pins 'before'
if (@($before).Count -ne 0) { Write-Host 'unexpected existing pins; close them first' -ForegroundColor Red }

$selRight = $SelX + $SelW
$selBottom = $SelY + $SelH
$selCenterX = $SelX + [int]($SelW / 2)
$selCenterY = $SelY + [int]($SelH / 2)

switch ($Scenario) {
    'basic' {
        New-Pin $SelX $SelY $SelW $SelH
        $pins = @(Report-Pins 'after-create')
        Save-ScreenPng ("{0}-basic-01-created.png" -f $Target)
        $pin = [IntPtr]$pins[0].Hwnd
        Write-Host ("PIN RECT {0}" -f (Get-PinRect $pin))

        # --- drag ---
        Invoke-Drag $selCenterX $selCenterY ($selCenterX + 120) ($selCenterY + 90)
        $pins = Report-Pins 'after-drag'
        Save-ScreenPng ("{0}-basic-02-dragged.png" -f $Target)
        Write-Host ("EXPECT MOVED dx=120 dy=90, PIN RECT {0}" -f (Get-PinRect ([IntPtr]$pins[0].Hwnd)))

        # --- wheel zoom out / in (anchor = cursor) ---
        $pin = [IntPtr]$pins[0].Hwnd
        $pinRect = New-Object ZeepCutT18+RECT
        [ZeepCutT18]::GetWindowRect($pin, [ref]$pinRect) | Out-Null
        $ax = $pinRect.Left + 120
        $ay = $pinRect.Top + 90
        Invoke-Wheel $ax $ay -2
        $pins = Report-Pins 'after-wheel-out'
        Save-ScreenPng ("{0}-basic-03-zoom-out.png" -f $Target)
        Invoke-Wheel $ax $ay 4
        $pins = Report-Pins 'after-wheel-in'
        Save-ScreenPng ("{0}-basic-04-zoom-in.png" -f $Target)

        # --- Ctrl+wheel opacity down to the 20% floor ---
        $pin = [IntPtr]$pins[0].Hwnd
        [ZeepCutT18]::GetWindowRect($pin, [ref]$pinRect) | Out-Null
        $ax = $pinRect.Left + 60
        $ay = $pinRect.Top + 60
        Invoke-Wheel $ax $ay -4 -Ctrl
        Save-ScreenPng ("{0}-basic-05-opacity-04.png" -f $Target)
        Invoke-Wheel $ax $ay -8 -Ctrl
        Save-ScreenPng ("{0}-basic-06-opacity-floor.png" -f $Target)
        Write-Host 'EXPECT PIN VISIBLY FADED, NEVER INVISIBLE (floor 20%)'
        Invoke-Wheel $ax $ay 10 -Ctrl
        Save-ScreenPng ("{0}-basic-07-opacity-restored.png" -f $Target)

        # --- right-click menu: 原始大小 / 复制 ---
        if (Click-PinMenuItem 'original-size' '原始大小') {
            Start-Sleep -Milliseconds 500
            Report-Pins 'after-original-size' | Out-Null
            Save-ScreenPng ("{0}-basic-08-original-size.png" -f $Target)
            Write-Host ("EXPECT SIZE RESTORED TO {0}x{1}" -f $SelW, $SelH)
        }
        if (Click-PinMenuItem 'copy' '复制') {
            Start-Sleep -Milliseconds 700
            Save-ClipboardPng ("{0}-basic-clipboard.png" -f $Target)
            Write-Host ("EXPECT CLIPBOARD IMAGE {0}x{1}" -f $SelW, $SelH)
        }

        # --- Esc closes the pin ---
        $pin = [IntPtr](Find-PinWindows)[0].Hwnd
        [ZeepCutT18]::SetForegroundWindow($pin) | Out-Null
        Start-Sleep -Milliseconds 250
        Send-Keys '{ESC}'
        Start-Sleep -Milliseconds 800
        $after = Report-Pins 'after-esc'
        Save-ScreenPng ("{0}-basic-09-after-esc.png" -f $Target)
        Write-Host ("EXPECT NO PIN WINDOWS: {0}" -f (@($after).Count -eq 0))
    }
    'lock' {
        New-Pin $SelX $SelY $SelW $SelH
        $pins = Report-Pins 'after-create'
        Save-ScreenPng ("{0}-lock-01-created.png" -f $Target)
        $before5 = Get-PinRect ([IntPtr]$pins[0].Hwnd)

        if (Click-PinMenuItem 'lock' '锁定位置') {
            Start-Sleep -Milliseconds 500
            Save-ScreenPng ("{0}-lock-02-locked.png" -f $Target)
            $pins = Report-Pins 'after-lock'
        }

        # drag attempt on a locked pin: must NOT move
        Invoke-Drag $selCenterX $selCenterY ($selCenterX + 160) ($selCenterY + 120)
        $afterDrag = Get-PinRect ([IntPtr](Find-PinWindows)[0].Hwnd)
        Save-ScreenPng ("{0}-lock-03-drag-rejected.png" -f $Target)
        Write-Host ("LOCK BEFORE={0} AFTER-DRAG-ATTEMPT={1} (must be identical)" -f $before5, $afterDrag)

        # wheel zoom still works while locked
        $pin = [IntPtr](Find-PinWindows)[0].Hwnd
        $r = New-Object ZeepCutT18+RECT
        [ZeepCutT18]::GetWindowRect($pin, [ref]$r) | Out-Null
        Invoke-Wheel ($r.Left + 40) ($r.Top + 40) 3
        $afterZoom = Get-PinRect ([IntPtr](Find-PinWindows)[0].Hwnd)
        Save-ScreenPng ("{0}-lock-04-zoom-while-locked.png" -f $Target)
        Write-Host ("LOCKED ZOOM BEFORE={0} AFTER={1} (must differ)" -f $afterDrag, $afterZoom)

        # unlock through the context menu, then drag works
        if (Click-PinMenuItem 'unlock' '解锁位置') {
            Start-Sleep -Milliseconds 500
            Save-ScreenPng ("{0}-lock-05-unlocked.png" -f $Target)
        }
        $pin = [IntPtr](Find-PinWindows)[0].Hwnd
        $u = New-Object ZeepCutT18+RECT
        [ZeepCutT18]::GetWindowRect($pin, [ref]$u) | Out-Null
        $ucx = [int](($u.Left + $u.Right) / 2)
        $ucy = [int](($u.Top + $u.Bottom) / 2)
        $unlockedBefore = Get-PinRect $pin
        Invoke-Drag $ucx $ucy ($ucx + 100) ($ucy + 70)
        $afterUnlocked = Get-PinRect ([IntPtr](Find-PinWindows)[0].Hwnd)
        Save-ScreenPng ("{0}-lock-06-moved-after-unlock.png" -f $Target)
        Write-Host ("UNLOCKED BEFORE={0} AFTER-DRAG={1} (must differ)" -f $unlockedBefore, $afterUnlocked)

        # close via the × button (top-right of the pin)
        $pin = [IntPtr](Find-PinWindows)[0].Hwnd
        $c = New-Object ZeepCutT18+RECT
        [ZeepCutT18]::GetWindowRect($pin, [ref]$c) | Out-Null
        Invoke-Click ($c.Right - 15) ($c.Top + 15)
        Start-Sleep -Milliseconds 700
        $after = Report-Pins 'after-close-button'
        Save-ScreenPng ("{0}-lock-07-after-close.png" -f $Target)
        Write-Host ("EXPECT NO PIN WINDOWS AFTER x CLICK: {0}" -f (@($after).Count -eq 0))
    }
    'menu' {
        New-Pin $SelX $SelY $SelW $SelH
        $pins = Report-Pins 'after-create'
        Save-ScreenPng ("{0}-menu-01-created.png" -f $Target)

        # zoom out twice so 原始大小 has something to restore
        $pin = [IntPtr]$pins[0].Hwnd
        $r = New-Object ZeepCutT18+RECT
        [ZeepCutT18]::GetWindowRect($pin, [ref]$r) | Out-Null
        Invoke-Wheel ($r.Left + 80) ($r.Top + 80) -3
        $zoomed = Get-PinRect ([IntPtr](Find-PinWindows)[0].Hwnd)
        Save-ScreenPng ("{0}-menu-02-zoomed-out.png" -f $Target)
        Write-Host ("ZOOMED {0} (smaller than {1}x{2})" -f $zoomed, $SelW, $SelH)

        if (Click-PinMenuItem 'original-size' '原始大小') {
            Start-Sleep -Milliseconds 600
            $restored = Get-PinRect ([IntPtr](Find-PinWindows)[0].Hwnd)
            Save-ScreenPng ("{0}-menu-03-original-size.png" -f $Target)
            Write-Host ("ORIGINAL SIZE RESTORED: {0} (expect {1}x{2})" -f $restored, $SelW, $SelH)
        }

        if (Click-PinMenuItem 'copy' '复制') {
            Start-Sleep -Milliseconds 700
            Save-ClipboardPng ("{0}-menu-clipboard.png" -f $Target)
        }

        if (Click-PinMenuItem 'save' '另存为…') {
            Start-Sleep -Milliseconds 1500
            Save-ScreenPng ("{0}-menu-04-save-dialog.png" -f $Target)
            $savePath = Join-Path $OutDir ("{0}-menu-pin-saved.png" -f $Target)
            if (Test-Path -LiteralPath $savePath) { Remove-Item -LiteralPath $savePath -Force }
            Set-DialogFileName $savePath | Out-Null
            Invoke-DialogSave | Out-Null
            if (Wait-ForFile $savePath) {
                $img = [System.Drawing.Image]::FromFile($savePath)
                Write-Host ("PIN SAVED FILE {0} {1}x{2} {3} bytes" -f $savePath, $img.Width, $img.Height, (Get-Item -LiteralPath $savePath).Length)
                $img.Dispose()
            } else { Write-Host 'PIN SAVED FILE NOT FOUND' -ForegroundColor Red }
            Save-ScreenPng ("{0}-menu-04b-after-save.png" -f $Target)
        }

        # 关闭 from the menu
        if (Click-PinMenuItem 'close' '关闭') {
            Start-Sleep -Milliseconds 800
        }
        $after = Report-Pins 'after-menu-close'
        Save-ScreenPng ("{0}-menu-05-closed.png" -f $Target)
        Write-Host ("EXPECT NO PIN WINDOWS: {0}" -f (@($after).Count -eq 0))
    }
    'crossscreen' {
        # Drag a pin from the 125% primary screen onto the 100% secondary screen (negative X):
        # the window rect must stay the same PHYSICAL size (DPI is re-read on the way over).
        New-Pin 120 400 300 240
        $pins = @(Report-Pins 'cross-create')
        Save-OtherScreenPng ("{0}-cross-01-on-primary.png" -f $Target) $true
        $pin = [IntPtr]$pins[0].Hwnd
        $r = New-Object ZeepCutT18+RECT
        [ZeepCutT18]::GetWindowRect($pin, [ref]$r) | Out-Null
        $before = Get-PinRect $pin
        Write-Host ("CROSS BEFORE={0}" -f $before)
        $scx = [int](($r.Left + $r.Right) / 2)
        $scy = [int](($r.Top + $r.Bottom) / 2)
        Invoke-Drag $scx $scy ($scx - 500) ($scy + 60)
        $after = Get-PinRect ([IntPtr](Find-PinWindows)[0].Hwnd)
        Save-OtherScreenPng ("{0}-cross-02-on-secondary.png" -f $Target) $false
        Write-Host ("CROSS AFTER={0} (expect same 300x240, x moved by -500)" -f $after)

        # zoom on the secondary screen must keep the 1:1 physical size too
        $pin = [IntPtr](Find-PinWindows)[0].Hwnd
        [ZeepCutT18]::GetWindowRect($pin, [ref]$r) | Out-Null
        Invoke-Wheel ($r.Left + 50) ($r.Top + 50) 2
        Write-Host ("CROSS ZOOM={0} (expect 1.21x => 363x290)" -f (Get-PinRect ([IntPtr](Find-PinWindows)[0].Hwnd)))
        Save-OtherScreenPng ("{0}-cross-03-zoomed-on-secondary.png" -f $Target) $false

        $pin = [IntPtr](Find-PinWindows)[0].Hwnd
        [ZeepCutT18]::SetForegroundWindow($pin) | Out-Null
        Start-Sleep -Milliseconds 250
        Send-Keys '{ESC}'
        Start-Sleep -Milliseconds 700
        $after2 = @(Report-Pins 'cross-after-esc')
        Write-Host ("EXPECT NO PINS: {0}" -f ($after2.Count -eq 0))
    }
    'limits' {
        # T19 size limits: wheel down until it stops (>= 40 px), wheel up until 8x.
        New-Pin $SelX $SelY 200 150
        $pins = @(Report-Pins 'limits-created')
        Save-ScreenPng ("{0}-limits-01-created.png" -f $Target)
        $pin = [IntPtr]$pins[0].Hwnd
        $r = New-Object ZeepCutT18+RECT
        [ZeepCutT18]::GetWindowRect($pin, [ref]$r) | Out-Null

        # shrink past the minimum
        for ($i = 0; $i -lt 24; $i++) {
            [ZeepCutT18]::SetCursorPos($r.Left + 100, $r.Top + 75) | Out-Null
            Start-Sleep -Milliseconds 60
            [ZeepCutT18]::Wheel(-120)
            Start-Sleep -Milliseconds 70
        }
        Start-Sleep -Milliseconds 500
        $pins = @(Report-Pins 'after-min-wheel')
        Save-ScreenPng ("{0}-limits-02-min.png" -f $Target)
        Write-Host 'EXPECT MIN SIZE 40x40 (never smaller, never invisible)'

        # grow past 8x
        $pin = [IntPtr](Find-PinWindows)[0].Hwnd
        [ZeepCutT18]::GetWindowRect($pin, [ref]$r) | Out-Null
        for ($i = 0; $i -lt 60; $i++) {
            [ZeepCutT18]::SetCursorPos($r.Left + 20, $r.Top + 20) | Out-Null
            Start-Sleep -Milliseconds 45
            [ZeepCutT18]::Wheel(120)
            Start-Sleep -Milliseconds 55
        }
        Start-Sleep -Milliseconds 500
        $pins = @(Report-Pins 'after-max-wheel')
        Save-ScreenPng ("{0}-limits-03-max.png" -f $Target)
        Write-Host 'EXPECT MAX SIZE 1600x1200 (200x150 x 8)'

        # back to original size for a clean close
        if (Click-PinMenuItem 'original-size' '原始大小') { Start-Sleep -Milliseconds 500 }
        $pins = @(Report-Pins 'after-original')
        $pin = [IntPtr](Find-PinWindows)[0].Hwnd
        $c = New-Object ZeepCutT18+RECT
        [ZeepCutT18]::GetWindowRect($pin, [ref]$c) | Out-Null
        Invoke-Click ($c.Right - 15) ($c.Top + 15)
        Start-Sleep -Milliseconds 700
        $after = @(Report-Pins 'after-close')
        Save-ScreenPng ("{0}-limits-04-closed.png" -f $Target)
        Write-Host ("EXPECT NO PINS: {0}" -f ($after.Count -eq 0))
    }
    'keep' {
        # Leave two pins on screen so the caller can verify "退出时全部关闭" (T20 scope, T18 exit path).
        New-Pin $SelX $SelY 300 220
        New-Pin ($SelX + 340) ($SelY + 40) 340 260
        $pins = @(Report-Pins 'keep')
        Save-ScreenPng ("{0}-keep-01-two-pins.png" -f $Target)
        Write-Host ("EXPECT 2 PINS STILL OPEN: {0}" -f ($pins.Count -eq 2))
    }
    'multi' {
        New-Pin $SelX $SelY 320 240
        $pins = Report-Pins 'pin-1'
        Save-ScreenPng ("{0}-multi-01-first.png" -f $Target)
        $first = Get-PinRect ([IntPtr]$pins[0].Hwnd)

        New-Pin ($SelX + 360) ($SelY + 40) 360 260
        $pins = Report-Pins 'pin-2'
        Save-ScreenPng ("{0}-multi-02-second.png" -f $Target)
        $pins = @($pins)
        if ($pins.Count -ne 2) { Write-Host 'EXPECT 2 PINS' -ForegroundColor Yellow }

        # drag the SECOND pin; the first must stay put
        $second = [IntPtr]$pins[1].Hwnd
        $r = New-Object ZeepCutT18+RECT
        [ZeepCutT18]::GetWindowRect($second, [ref]$r) | Out-Null
        $scx = [int](($r.Left + $r.Right) / 2)
        $scy = [int](($r.Top + $r.Bottom) / 2)
        Invoke-Drag $scx $scy ($scx - 80) ($scy + 140)
        $pins = Report-Pins 'after-drag-second'
        Save-ScreenPng ("{0}-multi-03-dragged-second.png" -f $Target)
        Write-Host ("FIRST BEFORE={0} FIRST NOW={1} (must be identical)" -f $first, (Get-PinRect ([IntPtr]$pins[0].Hwnd)))

        # close the first via ×, the second must survive
        $firstHwnd = [IntPtr]$pins[0].Hwnd
        $c = New-Object ZeepCutT18+RECT
        [ZeepCutT18]::GetWindowRect($firstHwnd, [ref]$c) | Out-Null
        Invoke-Click ($c.Right - 15) ($c.Top + 15)
        Start-Sleep -Milliseconds 700
        $pins = Report-Pins 'after-close-first'
        Save-ScreenPng ("{0}-multi-04-closed-first.png" -f $Target)
        Write-Host ("EXPECT 1 PIN LEFT: {0}" -f (@($pins).Count -eq 1))

        # Esc closes the last one
        $last = [IntPtr]$pins[0].Hwnd
        [ZeepCutT18]::SetForegroundWindow($last) | Out-Null
        Start-Sleep -Milliseconds 250
        Send-Keys '{ESC}'
        Start-Sleep -Milliseconds 700
        $after = Report-Pins 'after-esc'
        Save-ScreenPng ("{0}-multi-05-after-esc.png" -f $Target)
        Write-Host ("EXPECT NO PINS: {0}" -f (@($after).Count -eq 0))
    }
}

Write-Host 'DONE'
