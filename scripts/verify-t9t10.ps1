# T9/T10 desktop verification driver.
#
# One process runs the whole sequence so no agent round-trip is needed while the
# overlay is open. All coordinates are physical screen pixels (PerMonitorV2).
#
# Scenarios:
#   selection      drag select, move, resize, Enter, Esc (default)
#   reverse        drag bottom-right -> top-left (any-direction drag)
#   window-click   single click adopts the hovered window candidate
#   tiny           3x3 drag on empty desktop is below the minimum size -> no selection/toolbar
#   toolbar-above  select near the screen bottom (toolbar goes above), click Cancel via UI Automation
#   handles        resize through all eight handles in one session
#   drag-badge     capture the size badge while the left button is still down
#
# Examples:
#   -Target primary
#   -Target secondary
#   -Target primary -Scenario reverse
#   -Target primary -Scenario window-click -ClickX 1200 -ClickY 700
#   -Target primary -Scenario tiny
#   -Target primary -Scenario toolbar-above
param(
    [ValidateSet('primary','secondary')][string]$Target = 'primary',
    [ValidateSet('selection','reverse','window-click','tiny','toolbar-above','handles','drag-badge')][string]$Scenario = 'selection',
    [string]$OutDir = '',
    [int]$StartX = [int]::MinValue,
    [int]$StartY = [int]::MinValue,
    [int]$EndX = [int]::MinValue,
    [int]$EndY = [int]::MinValue,
    [int]$MoveDx = 200,
    [int]$MoveDy = 100,
    [int]$ResizeDx = 100,
    [int]$ResizeDy = 100,
    [int]$ClickX = [int]::MinValue,
    [int]$ClickY = [int]::MinValue
)
$ErrorActionPreference = 'Stop'

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Text;

public static class ZeepCutVerify
{
    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

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

    public const uint LEFTDOWN = 0x0002;
    public const uint LEFTUP = 0x0004;
}
'@

# -4 = DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2
[ZeepCutVerify]::SetProcessDpiAwarenessContext([IntPtr](-4)) | Out-Null
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

if ([string]::IsNullOrWhiteSpace($OutDir)) {
    $OutDir = Join-Path $env:TEMP 'zeepcut-t9t10'
}
New-Item -ItemType Directory -Path $OutDir -Force | Out-Null

$screen = $null
foreach ($s in [System.Windows.Forms.Screen]::AllScreens) {
    if ($Target -eq 'primary' -and $s.Primary) { $screen = $s; break }
    if ($Target -eq 'secondary' -and -not $s.Primary) { $screen = $s; break }
}
if (-not $screen) { Write-Host ("screen not found: {0}" -f $Target) -ForegroundColor Red; exit 1 }
$rect = $screen.Bounds
Write-Host ("SCREEN {0} origin=({1},{2}) {3}x{4} primary={5}" -f $screen.DeviceName, $rect.Left, $rect.Top, $rect.Width, $rect.Height, $screen.Primary)

$isPrimary = [bool]$screen.Primary

if ($StartX -eq [int]::MinValue) { if ($isPrimary) { $StartX = 300 } else { $StartX = -980 } }
if ($StartY -eq [int]::MinValue) { $StartY = 300 }
if ($EndX -eq [int]::MinValue) { if ($isPrimary) { $EndX = 940 } else { $EndX = -340 } }
if ($EndY -eq [int]::MinValue) { $EndY = 780 }
if ($Scenario -eq 'toolbar-above' -and $isPrimary) {
    if ($StartX -eq 300) { $StartX = 400 }
    if ($StartY -eq 300) { $StartY = 1150 }
    if ($EndX -eq 940) { $EndX = 1100 }
    if ($EndY -eq 780) { $EndY = 1400 }
}
if ($Scenario -eq 'tiny' -and $StartX -eq 300 -and $StartY -eq 300) {
    # 任务栏一带（y>=1380）没有窗口候选，3x3 拖动应被判定为无效。
    $StartX = 1000; $StartY = 1400; $EndX = 1003; $EndY = 1403
}
if ($ClickX -eq [int]::MinValue) { if ($isPrimary) { $ClickX = 1200 } else { $ClickX = -600 } }
if ($ClickY -eq [int]::MinValue) { $ClickY = 700 }

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
    [ZeepCutVerify]::SetCursorPos($x, $y) | Out-Null
    Start-Sleep -Milliseconds 120
}

function Invoke-Click([int]$x, [int]$y) {
    Move-Cursor $x $y
    [ZeepCutVerify]::mouse_event([ZeepCutVerify]::LEFTDOWN, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 80
    [ZeepCutVerify]::mouse_event([ZeepCutVerify]::LEFTUP, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 500
    Write-Host ("CLICK {0},{1}" -f $x, $y)
}

function Invoke-Drag([int]$x1, [int]$y1, [int]$x2, [int]$y2) {
    Move-Cursor $x1 $y1
    [ZeepCutVerify]::mouse_event([ZeepCutVerify]::LEFTDOWN, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 120
    $steps = 16
    for ($i = 1; $i -le $steps; $i++) {
        $cx = [int][Math]::Round($x1 + (($x2 - $x1) * $i / $steps))
        $cy = [int][Math]::Round($y1 + (($y2 - $y1) * $i / $steps))
        [ZeepCutVerify]::SetCursorPos($cx, $cy) | Out-Null
        Start-Sleep -Milliseconds 25
    }
    [ZeepCutVerify]::SetCursorPos($x2, $y2) | Out-Null
    Start-Sleep -Milliseconds 150
    [ZeepCutVerify]::mouse_event([ZeepCutVerify]::LEFTUP, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 450
    Write-Host ("DRAG {0},{1} -> {2},{3}" -f $x1, $y1, $x2, $y2)
}

function Send-Keys([string]$keys) {
    [System.Windows.Forms.SendKeys]::SendWait($keys)
    Start-Sleep -Milliseconds 200
    Write-Host ("SENT {0}" -f $keys)
}

function Get-CancelButtonRect {
    try {
        Add-Type -AssemblyName UIAutomationClient
        Add-Type -AssemblyName UIAutomationTypes

        $targetPid = (Get-Process ZeepCut -ErrorAction SilentlyContinue | Select-Object -First 1).Id
        if (-not $targetPid) {
            Write-Host 'UIA: ZeepCut process not found'
            return $null
        }

        # 不按完整中文标题找窗口（跨进程/编码容易出错），改用进程 id + 标题前缀。
        $script:overlayHwnd = [IntPtr]::Zero
        [ZeepCutVerify]::EnumWindows({ param($h, $p)
            $sb = New-Object System.Text.StringBuilder 256
            [void][ZeepCutVerify]::GetWindowText($h, $sb, 256)
            $procId = 0
            [void][ZeepCutVerify]::GetWindowThreadProcessId($h, [ref]$procId)
            if ($procId -eq $targetPid -and [ZeepCutVerify]::IsWindowVisible($h) -and $sb.ToString().StartsWith('Zeep Cut')) {
                $script:overlayHwnd = $h
            }
            return $true
        }, [IntPtr]::Zero) | Out-Null

        $hwnd = $script:overlayHwnd
        if ($hwnd -eq [IntPtr]::Zero) {
            Write-Host 'UIA: overlay window not found'
            return $null
        }
        $root = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)
        if (-not $root) {
            Write-Host 'UIA: AutomationElement.FromHandle returned null'
            return $null
        }
        $cond = New-Object System.Windows.Automation.AndCondition(
            (New-Object System.Windows.Automation.PropertyCondition(
                [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
                [System.Windows.Automation.ControlType]::Button)),
            (New-Object System.Windows.Automation.PropertyCondition(
                [System.Windows.Automation.AutomationElement]::NameProperty,
                '取消')))
        $button = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
        if (-not $button) {
            Write-Host 'UIA: Cancel button not found'
            return $null
        }
        $r = $button.Current.BoundingRectangle
        Write-Host ("UIA CANCEL BUTTON {0},{1} {2}x{3}" -f $r.X, $r.Y, $r.Width, $r.Height)
        return $r
    }
    catch {
        Write-Host ("UIA failed: {0}" -f $_.Exception.Message) -ForegroundColor Yellow
        return $null
    }
}

# 1. Trigger Alt+A with the cursor inside the target screen.
Move-Cursor ([int]($rect.Left + $rect.Width / 2)) ([int]($rect.Top + $rect.Height / 2))
Send-Keys '%a'
Start-Sleep -Milliseconds 1600

switch ($Scenario) {
    'reverse' {
        Invoke-Drag $EndX $EndY $StartX $StartY
        $width = [Math]::Abs($EndX - $StartX)
        $height = [Math]::Abs($EndY - $StartY)
        Write-Host ("EXPECT REVERSE SELECTION {0}x{1} at ({2},{3})" -f $width, $height, ([Math]::Min($StartX, $EndX)), ([Math]::Min($StartY, $EndY)))
        Save-ScreenPng ("{0}-reverse-drag.png" -f $Target)
        Send-Keys '{ESC}'
        Start-Sleep -Milliseconds 900
        Save-ScreenPng ("{0}-reverse-after-esc.png" -f $Target)
        exit 0
    }

    'window-click' {
        Move-Cursor $ClickX $ClickY
        Start-Sleep -Milliseconds 300
        Invoke-Click $ClickX $ClickY
        Save-ScreenPng ("{0}-window-click.png" -f $Target)
        Send-Keys '{ESC}'
        Start-Sleep -Milliseconds 900
        Save-ScreenPng ("{0}-window-click-after-esc.png" -f $Target)
        exit 0
    }

    'tiny' {
        Invoke-Drag $StartX $StartY $EndX $EndY
        Write-Host ("EXPECT TINY DRAG REJECTED ({0},{1}) -> ({2},{3})" -f $StartX, $StartY, $EndX, $EndY)
        Save-ScreenPng ("{0}-tiny-drag.png" -f $Target)
        Send-Keys '{ESC}'
        Start-Sleep -Milliseconds 900
        Save-ScreenPng ("{0}-tiny-after-esc.png" -f $Target)
        exit 0
    }

    'drag-badge' {
        # 按住左键拖到一半时截图：此时工具栏尚未出现，选区右上方应显示尺寸文字。
        Move-Cursor $StartX $StartY
        [ZeepCutVerify]::mouse_event([ZeepCutVerify]::LEFTDOWN, 0, 0, 0, [UIntPtr]::Zero)
        Start-Sleep -Milliseconds 120
        for ($i = 1; $i -le 16; $i++) {
            $cx = [int][Math]::Round($StartX + (($EndX - $StartX) * $i / 16))
            $cy = [int][Math]::Round($StartY + (($EndY - $StartY) * $i / 16))
            [ZeepCutVerify]::SetCursorPos($cx, $cy) | Out-Null
            Start-Sleep -Milliseconds 25
        }
        Start-Sleep -Milliseconds 400
        Write-Host ("EXPECT SIZE BADGE {0}x{1} while dragging" -f ([Math]::Abs($EndX - $StartX)), ([Math]::Abs($EndY - $StartY)))
        Save-ScreenPng ("{0}-drag-badge.png" -f $Target)
        [ZeepCutVerify]::mouse_event([ZeepCutVerify]::LEFTUP, 0, 0, 0, [UIntPtr]::Zero)
        Start-Sleep -Milliseconds 500
        Save-ScreenPng ("{0}-drag-badge-released.png" -f $Target)
        Send-Keys '{ESC}'
        Start-Sleep -Milliseconds 900
        Save-ScreenPng ("{0}-drag-badge-after-esc.png" -f $Target)
        exit 0
    }

    'handles' {
        Invoke-Drag $StartX $StartY $EndX $EndY
        $l = [Math]::Min($StartX, $EndX)
        $t = [Math]::Min($StartY, $EndY)
        $r = [Math]::Max($StartX, $EndX)
        $b = [Math]::Max($StartY, $EndY)
        $midX = [int](($l + $r) / 2)
        $midY = [int](($t + $b) / 2)

        # Right / Left / Top / Bottom
        Invoke-Drag ($r - 2) $midY ($r + 40) $midY; $r = $r + 40
        Invoke-Drag ($l + 2) $midY ($l - 20) $midY; $l = $l - 20
        Invoke-Drag $midX ($t + 2) $midX ($t - 30); $t = $t - 30
        Invoke-Drag $midX ($b - 2) $midX ($b + 30); $b = $b + 30

        # Four corners
        Invoke-Drag ($l + 2) ($t + 2) ($l - 10) ($t - 20); $l = $l - 10; $t = $t - 20
        Invoke-Drag ($r - 2) ($t + 2) ($r + 20) ($t - 10); $r = $r + 20; $t = $t - 10
        Invoke-Drag ($r - 2) ($b - 2) ($r + 10) ($b + 20); $r = $r + 10; $b = $b + 20
        Invoke-Drag ($l + 2) ($b - 2) ($l - 10) ($b + 20); $l = $l - 10; $b = $b + 20

        Write-Host ("EXPECT FINAL HANDLE RESULT ({0},{1}) {2}x{3}" -f $l, $t, ($r - $l), ($b - $t))
        Save-ScreenPng ("{0}-handles.png" -f $Target)
        Send-Keys '{ESC}'
        Start-Sleep -Milliseconds 900
        Save-ScreenPng ("{0}-handles-after-esc.png" -f $Target)
        exit 0
    }

    'toolbar-above' {
        Invoke-Drag $StartX $StartY $EndX $EndY
        Write-Host ("EXPECT TOOLBAR ABOVE SELECTION ({0},{1}) -> ({2},{3})" -f $StartX, $StartY, $EndX, $EndY)
        Save-ScreenPng ("{0}-toolbar-above.png" -f $Target)

        $r = Get-CancelButtonRect
        if (-not $r) {
            # 本机 DSH 沙箱的 PowerShell 与目标窗口不在同一 window station，
            # FindWindow/UIA 看不到遮罩；此时退回 Esc 关闭，取消按钮另行按日志坐标点击。
            Write-Host 'CANCEL BUTTON RECT UNAVAILABLE -> fallback to Esc' -ForegroundColor Yellow
            Send-Keys '{ESC}'
            Start-Sleep -Milliseconds 900
            Save-ScreenPng ("{0}-after-fallback-esc.png" -f $Target)
            exit 0
        }
        $clickX = [int]($r.X + ($r.Width / 2))
        $clickY = [int]($r.Y + ($r.Height / 2))
        Write-Host ("CANCEL CLICK {0},{1}" -f $clickX, $clickY)
        Invoke-Click $clickX $clickY
        Start-Sleep -Milliseconds 600
        Save-ScreenPng ("{0}-after-cancel-button.png" -f $Target)
        exit 0
    }
}

# Default scenario: drag select, move, resize, Enter placeholder, Esc.
Invoke-Drag $StartX $StartY $EndX $EndY
$width = [Math]::Abs($EndX - $StartX)
$height = [Math]::Abs($EndY - $StartY)
Write-Host ("EXPECT SELECTION {0}x{1} physical" -f $width, $height)
Save-ScreenPng ("{0}-selection.png" -f $Target)

$left = [Math]::Min($StartX, $EndX)
$top = [Math]::Min($StartY, $EndY)
$right = [Math]::Max($StartX, $EndX)
$bottom = [Math]::Max($StartY, $EndY)
if ($MoveDx -ne 0 -or $MoveDy -ne 0) {
    $cx = [int](($left + $right) / 2)
    $cy = [int](($top + $bottom) / 2)
    Invoke-Drag $cx $cy ($cx + $MoveDx) ($cy + $MoveDy)
    Write-Host ("EXPECT MOVED TO ({0},{1}) {2}x{3}" -f ($left + $MoveDx), ($top + $MoveDy), $width, $height)
    Save-ScreenPng ("{0}-moved.png" -f $Target)
}

if ($ResizeDx -ne 0 -or $ResizeDy -ne 0) {
    $rx = $right + $MoveDx - 2
    $ry = $bottom + $MoveDy - 2
    Invoke-Drag $rx $ry ($rx + $ResizeDx) ($ry + $ResizeDy)
    Write-Host ("EXPECT RESIZED TO ({0},{1}) {2}x{3}" -f ($left + $MoveDx), ($top + $MoveDy), ($width + $ResizeDx - 2), ($height + $ResizeDy - 2))
    Save-ScreenPng ("{0}-resized.png" -f $Target)
}

Send-Keys '{ENTER}'
Start-Sleep -Milliseconds 300
Save-ScreenPng ("{0}-after-enter.png" -f $Target)

Send-Keys '{ESC}'
Start-Sleep -Milliseconds 900
Save-ScreenPng ("{0}-after-esc.png" -f $Target)
