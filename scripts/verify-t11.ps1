# T11 desktop verification driver (copy / save / cancel / pin / clipboard busy).
#
# One process runs a whole scenario so no agent round-trip is needed while the overlay
# is open. All coordinates are physical screen pixels (PerMonitorV2).
#
# Scenarios:
#   copy          drag select then press Enter (real copy); overlay must close
#   copy-button   drag select then click the toolbar "复制" button via UI Automation
#   copy-edge     drag select touching the right edge / near the bottom (primary only)
#   save-png      click "另存为", paste a temp path into the file-name field, verify PNG
#   save-jpeg     same but with a .jpg path (JPEG encoding + no alpha)
#   cancel-save   open "另存为" then click Cancel; overlay must stay, no file, no record
#   pin           click "贴图"; a borderless topmost pin window must appear; Esc closes it
#   copy-busy     hold the clipboard open, Enter must fail and keep the overlay;
#                 release, Enter again must succeed and close the overlay
#
# The Win11 file dialog's file-name field is DirectUI (no UIA ValuePattern, and
# SetWindowText on the child Edit does not change what gets saved), so the file
# name is pasted from the clipboard instead of typed (also avoids IME rewriting
# `C:\...` to `C：、...`). Save/Cancel are clicked through the dialog's real
# child Button hwnds (BM_CLICK).
#
# Examples:
#   -Target primary -Scenario copy
#   -Target secondary -Scenario copy
#   -Target primary -Scenario save-png -SavePath C:\Users\...\Temp\zeepcut-t11\save.png
param(
    [ValidateSet('primary','secondary')][string]$Target = 'primary',
    [ValidateSet('copy','copy-button','copy-edge','save-png','save-jpeg','cancel-save','pin','copy-busy')][string]$Scenario = 'copy',
    [string]$OutDir = '',
    [string]$SavePath = '',
    [int]$StartX = [int]::MinValue,
    [int]$StartY = [int]::MinValue,
    [int]$EndX = [int]::MinValue,
    [int]$EndY = [int]::MinValue
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

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
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
    public static extern bool EnumChildWindows(IntPtr parent, EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern int GetDlgCtrlID(IntPtr hWnd);

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
    public static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool CloseClipboard();

    [DllImport("user32.dll")]
    public static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    public const uint BM_CLICK = 0x00F5;

    public const uint LEFTDOWN = 0x0002;
    public const uint LEFTUP = 0x0004;
}
'@

# -4 = DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2
[ZeepCutVerify]::SetProcessDpiAwarenessContext([IntPtr](-4)) | Out-Null
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

if ([string]::IsNullOrWhiteSpace($OutDir)) {
    $OutDir = Join-Path $env:TEMP 'zeepcut-t11'
}
New-Item -ItemType Directory -Path $OutDir -Force | Out-Null

$screen = $null
foreach ($s in [System.Windows.Forms.Screen]::AllScreens) {
    if ($Target -eq 'primary' -and $s.Primary) { $screen = $s; break }
    if ($Target -eq 'secondary' -and -not $s.Primary) { $screen = $s; break }
}
if (-not $screen) { Write-Host ("SCREEN NOT FOUND {0}" -f $Target) -ForegroundColor Red; exit 1 }
$rect = $screen.Bounds
$isPrimary = [bool]$screen.Primary
Write-Host ("SCREEN {0} origin=({1},{2}) {3}x{4} primary={5}" -f $screen.DeviceName, $rect.Left, $rect.Top, $rect.Width, $rect.Height, $isPrimary)

if ($StartX -eq [int]::MinValue) { $StartX = if ($isPrimary) { 600 } else { -980 } }
if ($StartY -eq [int]::MinValue) { $StartY = if ($isPrimary) { 400 } else { 300 } }
if ($EndX -eq [int]::MinValue) { $EndX = if ($isPrimary) { 1200 } else { -340 } }
if ($EndY -eq [int]::MinValue) { $EndY = if ($isPrimary) { 700 } else { 780 } }
if ($Scenario -eq 'copy-edge') {
    $StartX = 2200; $StartY = 1100; $EndX = 2559; $EndY = 1379
}

$selLeft = [Math]::Min($StartX, $EndX)
$selTop = [Math]::Min($StartY, $EndY)
$selWidth = [Math]::Abs($EndX - $StartX)
$selHeight = [Math]::Abs($EndY - $StartY)

$script:targetPid = 0
function Get-AppPid {
    $p = Get-Process ZeepCut -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($p) { return [int]$p.Id }
    return 0
}

function Get-AppWindows {
    $script:windowList = New-Object System.Collections.ArrayList
    [ZeepCutVerify]::EnumWindows({ param($h, $p)
        $procId = 0
        [void][ZeepCutVerify]::GetWindowThreadProcessId($h, [ref]$procId)
        if ($procId -eq $script:targetPid -and [ZeepCutVerify]::IsWindowVisible($h)) {
            $sb = New-Object System.Text.StringBuilder 256
            [void][ZeepCutVerify]::GetWindowText($h, $sb, 256)
            $r = New-Object 'ZeepCutVerify+RECT'
            if ([ZeepCutVerify]::GetWindowRect($h, [ref]$r)) {
                [void]$script:windowList.Add([pscustomobject]@{
                    Hwnd = $h
                    Title = $sb.ToString()
                    Left = $r.Left
                    Top = $r.Top
                    Width = $r.Right - $r.Left
                    Height = $r.Bottom - $r.Top
                })
            }
        }
        return $true
    }, [IntPtr]::Zero) | Out-Null
    return $script:windowList
}

function Show-AppWindows([string]$stage) {
    $windows = Get-AppWindows
    if ($windows.Count -eq 0) {
        Write-Host ("WINDOWS {0}: none" -f $stage)
        return
    }
    foreach ($w in $windows) {
        Write-Host ("WINDOW {0}: '{1}' ({2},{3}) {4}x{5}" -f $stage, $w.Title, $w.Left, $w.Top, $w.Width, $w.Height)
    }
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
    Start-Sleep -Milliseconds 250
    Write-Host ("SENT {0}" -f $keys)
}

function Get-OverlayButtonRect([string]$name) {
    for ($attempt = 1; $attempt -le 6; $attempt++) {
        try {
            Add-Type -AssemblyName UIAutomationClient
            Add-Type -AssemblyName UIAutomationTypes

            $script:overlayHwnd = [IntPtr]::Zero
            [ZeepCutVerify]::EnumWindows({ param($h, $p)
                $sb = New-Object System.Text.StringBuilder 256
                [void][ZeepCutVerify]::GetWindowText($h, $sb, 256)
                $procId = 0
                [void][ZeepCutVerify]::GetWindowThreadProcessId($h, [ref]$procId)
                if ($procId -eq $script:targetPid -and [ZeepCutVerify]::IsWindowVisible($h) -and $sb.ToString().StartsWith('Zeep Cut 截图遮罩')) {
                    $script:overlayHwnd = $h
                }
                return $true
            }, [IntPtr]::Zero) | Out-Null

            $hwnd = $script:overlayHwnd
            if ($hwnd -eq [IntPtr]::Zero) {
                Start-Sleep -Milliseconds 300
                continue
            }

            $root = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)
            if (-not $root) {
                Start-Sleep -Milliseconds 300
                continue
            }

            $cond = New-Object System.Windows.Automation.AndCondition(
                (New-Object System.Windows.Automation.PropertyCondition(
                    [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
                    [System.Windows.Automation.ControlType]::Button)),
                (New-Object System.Windows.Automation.PropertyCondition(
                    [System.Windows.Automation.AutomationElement]::NameProperty,
                    $name)))
            $button = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
            if (-not $button) {
                Start-Sleep -Milliseconds 300
                continue
            }

            $r = $button.Current.BoundingRectangle
            Write-Host ("UIA BUTTON '{0}' {1},{2} {3}x{4}" -f $name, $r.X, $r.Y, $r.Width, $r.Height)
            return $r
        }
        catch {
            Write-Host ("UIA attempt {0} failed: {1}" -f $attempt, $_.Exception.Message) -ForegroundColor Yellow
            Start-Sleep -Milliseconds 300
        }
    }
    return $null
}

function Click-OverlayButton([string]$name) {
    $r = Get-OverlayButtonRect $name
    if (-not $r) {
        Write-Host ("BUTTON NOT FOUND '{0}'" -f $name) -ForegroundColor Red
        return $false
    }
    Invoke-Click ([int]($r.X + ($r.Width / 2))) ([int]($r.Y + ($r.Height / 2)))
    return $true
}

function Get-OverlayWindow {
    $windows = Get-AppWindows
    return $windows | Where-Object { $_.Title -like 'Zeep Cut 截图遮罩*' } | Select-Object -First 1
}

function Get-PinWindow {
    $windows = Get-AppWindows
    return $windows | Where-Object { $_.Title -eq 'Zeep Cut 贴图' } | Select-Object -First 1
}

function Test-ImageFile([string]$path, [string]$expectedKind) {
    if (-not (Test-Path $path)) {
        Write-Host ("FILE MISSING {0}" -f $path) -ForegroundColor Red
        return
    }
    $bytes = [System.IO.File]::ReadAllBytes($path)
    $image = [System.Drawing.Image]::FromFile($path)
    try {
        Write-Host ("FILE OK {0} {1}x{2} bytes={3} format={4}" -f $path, $image.Width, $image.Height, $bytes.Length, $image.RawFormat.Guid)
        if ($expectedKind -eq 'jpeg') {
            if ($bytes.Length -lt 2 -or $bytes[0] -ne 0xFF -or $bytes[1] -ne 0xD8) {
                Write-Host 'JPEG MAGIC MISSING' -ForegroundColor Red
            }
            else {
                Write-Host 'JPEG MAGIC OK (FF D8)'
            }
        }
        if ($expectedKind -eq 'png') {
            if ($bytes.Length -lt 8 -or $bytes[0] -ne 0x89 -or $bytes[1] -ne 0x50 -or $bytes[2] -ne 0x4E -or $bytes[3] -ne 0x47) {
                Write-Host 'PNG MAGIC MISSING' -ForegroundColor Red
            }
            else {
                Write-Host 'PNG MAGIC OK'
            }
        }
    }
    finally {
        $image.Dispose()
    }
}

function Wait-ForFile([string]$path, [int]$timeoutMs = 4000) {
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    while ($sw.ElapsedMilliseconds -lt $timeoutMs) {
        if (Test-Path $path) { return $true }
        Start-Sleep -Milliseconds 200
    }
    return $false
}

# --- SaveFileDialog helpers -------------------------------------------------
# The Chinese IME rewrites typed paths (C:\... -> C：、...), and the Win11 file
# dialog exposes its file-name field and buttons as DirectUI panes without UIA
# Value/Invoke patterns. So drive the dialog through its real child windows:
# WM_SETTEXT on the file-name Edit (control id 1001) and BM_CLICK on the
# Save/Cancel buttons (control id 1/2). Verification-only code; the app itself
# stays WPF + Win32.

function Find-AppDialogHwnd {
    $script:dialogHwnd = [IntPtr]::Zero
    [ZeepCutVerify]::EnumWindows({ param($h, $p)
        $procId = 0
        [void][ZeepCutVerify]::GetWindowThreadProcessId($h, [ref]$procId)
        if ($procId -ne $script:targetPid -or -not [ZeepCutVerify]::IsWindowVisible($h)) { return $true }
        $cls = New-Object System.Text.StringBuilder 256
        [void][ZeepCutVerify]::GetClassName($h, $cls, 256)
        if ($cls.ToString() -eq '#32770') { $script:dialogHwnd = $h }
        return $true
    }, [IntPtr]::Zero) | Out-Null
    return $script:dialogHwnd
}

function Wait-AppDialogHwnd([int]$timeoutMs = 5000) {
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    while ($sw.ElapsedMilliseconds -lt $timeoutMs) {
        $h = Find-AppDialogHwnd
        if ($h -ne [IntPtr]::Zero) {
            Start-Sleep -Milliseconds 300
            return $h
        }
        Start-Sleep -Milliseconds 200
    }
    return [IntPtr]::Zero
}

function Find-DialogChild([int]$controlId, [string]$className) {
    $hwnd = Wait-AppDialogHwnd
    if ($hwnd -eq [IntPtr]::Zero) {
        Write-Host 'DIALOG NOT FOUND' -ForegroundColor Red
        return [IntPtr]::Zero
    }

    $script:dialogChild = [IntPtr]::Zero
    [ZeepCutVerify]::EnumChildWindows($hwnd, { param($h, $p)
        if ([ZeepCutVerify]::GetDlgCtrlID($h) -ne $controlId) { return $true }
        $cls = New-Object System.Text.StringBuilder 256
        [void][ZeepCutVerify]::GetClassName($h, $cls, 256)
        if ($className -eq '' -or $cls.ToString() -eq $className) { $script:dialogChild = $h }
        return $true
    }, [IntPtr]::Zero) | Out-Null
    return $script:dialogChild
}

function Set-DialogFileName([string]$path) {
    # 可见的"文件名"是 DirectUI 控件：对子 Edit 调 SetWindowText 不会改变对话框
    # 实际保存的名称（实测仍保存默认名）。改为用 UIA 找到该字段、点击聚焦后用
    # 剪贴板粘贴路径——完全绕过中文输入法对 `C:\...` 的改写。
    $hwnd = Wait-AppDialogHwnd
    if ($hwnd -eq [IntPtr]::Zero) {
        Write-Host 'DIALOG NOT FOUND' -ForegroundColor Red
        return $false
    }

    Add-Type -AssemblyName UIAutomationClient
    Add-Type -AssemblyName UIAutomationTypes
    $root = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)
    if (-not $root) {
        Write-Host 'DIALOG UIA ROOT NULL' -ForegroundColor Red
        return $false
    }

    $fieldCond = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::Pane)),
        (New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::AutomationIdProperty,
            '1001')),
        (New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ClassNameProperty,
            'Edit')))
    $field = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $fieldCond)
    if (-not $field) {
        Write-Host 'DIALOG FILE NAME FIELD NOT FOUND' -ForegroundColor Red
        return $false
    }

    $r = $field.Current.BoundingRectangle
    Write-Host ("DIALOG FILE NAME FIELD {0},{1} {2}x{3} current=[{4}]" -f $r.X, $r.Y, $r.Width, $r.Height, $field.Current.Name)
    Invoke-Click ([int]($r.X + $r.Width / 2)) ([int]($r.Y + $r.Height / 2))

    [System.Windows.Forms.Clipboard]::SetText($path)
    Start-Sleep -Milliseconds 200
    Send-Keys '^a'
    Send-Keys '^v'
    Start-Sleep -Milliseconds 400

    $field = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $fieldCond)
    if ($field) {
        Write-Host ("DIALOG FILE NAME NOW [{0}]" -f $field.Current.Name)
        return ($field.Current.Name -eq $path)
    }

    return $false
}

function Invoke-DialogSave {
    $button = Find-DialogChild 1 'Button'
    if ($button -eq [IntPtr]::Zero) {
        Write-Host 'DIALOG SAVE BUTTON NOT FOUND' -ForegroundColor Red
        return $false
    }

    [ZeepCutVerify]::SendMessage($button, [ZeepCutVerify]::BM_CLICK, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
    Write-Host 'DIALOG SAVE CLICKED'
    return $true
}

function Invoke-DialogCancel {
    $button = Find-DialogChild 2 'Button'
    if ($button -eq [IntPtr]::Zero) {
        Write-Host 'DIALOG CANCEL BUTTON NOT FOUND' -ForegroundColor Red
        return $false
    }

    [ZeepCutVerify]::SendMessage($button, [ZeepCutVerify]::BM_CLICK, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
    Write-Host 'DIALOG CANCEL CLICKED'
    return $true
}

# --- trigger overlay and select ---
Move-Cursor ([int]($rect.Left + $rect.Width / 2)) ([int]($rect.Top + $rect.Height / 2))
Send-Keys '%a'
Start-Sleep -Milliseconds 1600
$script:targetPid = Get-AppPid
Write-Host ("APP PID {0}" -f $script:targetPid)
Show-AppWindows 'after-trigger'

Invoke-Drag $StartX $StartY $EndX $EndY
Write-Host ("EXPECT SELECTION ({0},{1}) {2}x{3}" -f $selLeft, $selTop, $selWidth, $selHeight)
Save-ScreenPng ("{0}-{1}-selection.png" -f $Target, $Scenario)
Show-AppWindows 'after-selection'

switch ($Scenario) {
    'copy' {
        Send-Keys '{ENTER}'
        Start-Sleep -Milliseconds 1200
        Save-ScreenPng ("{0}-copy-after.png" -f $Target)
        Show-AppWindows 'after-copy'
        if (Get-OverlayWindow) { Write-Host 'OVERLAY STILL OPEN (copy failed)' -ForegroundColor Red }
        else { Write-Host 'OVERLAY CLOSED (copy success)' }
        exit 0
    }

    'copy-button' {
        if (-not (Click-OverlayButton '复制')) { exit 3 }
        Start-Sleep -Milliseconds 1200
        Save-ScreenPng ("{0}-copy-button-after.png" -f $Target)
        Show-AppWindows 'after-copy-button'
        if (Get-OverlayWindow) { Write-Host 'OVERLAY STILL OPEN (copy button failed)' -ForegroundColor Red }
        else { Write-Host 'OVERLAY CLOSED (copy button success)' }
        exit 0
    }

    'copy-edge' {
        Send-Keys '{ENTER}'
        Start-Sleep -Milliseconds 1200
        Save-ScreenPng ("{0}-copy-edge-after.png" -f $Target)
        Show-AppWindows 'after-copy-edge'
        if (Get-OverlayWindow) { Write-Host 'OVERLAY STILL OPEN (copy edge failed)' -ForegroundColor Red }
        else { Write-Host 'OVERLAY CLOSED (copy edge success)' }
        exit 0
    }

    'save-png' {
        if ([string]::IsNullOrWhiteSpace($SavePath)) {
            $SavePath = Join-Path $env:TEMP ("zeepcut-t11\save-{0}.png" -f (Get-Date -Format 'yyyyMMdd-HHmmss'))
        }
        $dir = Split-Path -Parent $SavePath
        if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
        if (Test-Path $SavePath) { Remove-Item $SavePath -Force }

        if (-not (Click-OverlayButton '另存为')) { exit 3 }
        Start-Sleep -Milliseconds 1000
        Save-ScreenPng ("{0}-save-dialog.png" -f $Target)
        if (-not (Set-DialogFileName $SavePath)) {
            Invoke-DialogCancel | Out-Null
            exit 4
        }
        Save-ScreenPng ("{0}-save-dialog-filled.png" -f $Target)
        if (-not (Invoke-DialogSave)) {
            Write-Host 'SAVE BUTTON INVOKE FAILED' -ForegroundColor Red
            exit 4
        }
        Start-Sleep -Milliseconds 1200
        Save-ScreenPng ("{0}-save-after.png" -f $Target)
        Show-AppWindows 'after-save'
        if (Wait-ForFile $SavePath) {
            Test-ImageFile $SavePath 'png'
            Copy-Item -Path $SavePath -Destination (Join-Path $OutDir ("{0}-saved.png" -f $Target)) -Force
        }
        if (Get-OverlayWindow) { Write-Host 'OVERLAY STILL OPEN (save failed)' -ForegroundColor Red }
        else { Write-Host 'OVERLAY CLOSED (save success)' }
        exit 0
    }

    'save-jpeg' {
        if ([string]::IsNullOrWhiteSpace($SavePath)) {
            $SavePath = Join-Path $env:TEMP ("zeepcut-t11\save-{0}.jpg" -f (Get-Date -Format 'yyyyMMdd-HHmmss'))
        }
        $dir = Split-Path -Parent $SavePath
        if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
        if (Test-Path $SavePath) { Remove-Item $SavePath -Force }

        if (-not (Click-OverlayButton '另存为')) { exit 3 }
        Start-Sleep -Milliseconds 1000
        Save-ScreenPng ("{0}-jpeg-dialog.png" -f $Target)
        if (-not (Set-DialogFileName $SavePath)) {
            Invoke-DialogCancel | Out-Null
            exit 4
        }
        Save-ScreenPng ("{0}-jpeg-dialog-filled.png" -f $Target)
        if (-not (Invoke-DialogSave)) {
            Write-Host 'JPEG SAVE BUTTON INVOKE FAILED' -ForegroundColor Red
            exit 4
        }
        Start-Sleep -Milliseconds 1200
        Save-ScreenPng ("{0}-jpeg-after.png" -f $Target)
        Show-AppWindows 'after-jpeg'
        if (Wait-ForFile $SavePath) {
            Test-ImageFile $SavePath 'jpeg'
            Copy-Item -Path $SavePath -Destination (Join-Path $OutDir ("{0}-saved.jpg" -f $Target)) -Force
        }
        if (Get-OverlayWindow) { Write-Host 'OVERLAY STILL OPEN (jpeg save failed)' -ForegroundColor Red }
        else { Write-Host 'OVERLAY CLOSED (jpeg save success)' }
        exit 0
    }

    'cancel-save' {
        $probe = Join-Path $env:TEMP ("zeepcut-t11\cancel-probe-{0}.png" -f (Get-Date -Format 'yyyyMMdd-HHmmss'))
        if (Test-Path $probe) { Remove-Item $probe -Force }

        if (-not (Click-OverlayButton '另存为')) { exit 3 }
        Start-Sleep -Milliseconds 1000
        Save-ScreenPng ("{0}-cancel-dialog.png" -f $Target)
        if (-not (Invoke-DialogCancel)) {
            Send-Keys '{ESC}'
        }
        Start-Sleep -Milliseconds 1000
        Save-ScreenPng ("{0}-cancel-after-dialog.png" -f $Target)
        Show-AppWindows 'after-cancel-dialog'
        if (Get-OverlayWindow) { Write-Host 'OVERLAY STILL OPEN (expected after cancel)' }
        else { Write-Host 'OVERLAY CLOSED (WRONG after cancel)' -ForegroundColor Red }
        if (Test-Path $probe) { Write-Host 'PROBE FILE EXISTS (WRONG)' -ForegroundColor Red } else { Write-Host 'PROBE FILE ABSENT (ok)' }

        Send-Keys '{ESC}'
        Start-Sleep -Milliseconds 900
        Save-ScreenPng ("{0}-cancel-final.png" -f $Target)
        Show-AppWindows 'after-final-esc'
        if (Get-OverlayWindow) { Write-Host 'OVERLAY STILL OPEN after final Esc (WRONG)' -ForegroundColor Red }
        else { Write-Host 'OVERLAY CLOSED after final Esc (ok)' }
        exit 0
    }

    'pin' {
        if (-not (Click-OverlayButton '贴图')) { exit 3 }
        Start-Sleep -Milliseconds 1500
        Save-ScreenPng ("{0}-pin.png" -f $Target)
        Show-AppWindows 'after-pin'
        $pin = Get-PinWindow
        if (-not $pin) {
            Write-Host 'PIN WINDOW MISSING' -ForegroundColor Red
            exit 4
        }
        Write-Host ("PIN WINDOW ({0},{1}) {2}x{3}" -f $pin.Left, $pin.Top, $pin.Width, $pin.Height)
        if ($pin.Left -eq $selLeft -and $pin.Top -eq $selTop -and $pin.Width -eq $selWidth -and $pin.Height -eq $selHeight) {
            Write-Host 'PIN RECT MATCHES SELECTION'
        }
        else {
            Write-Host ("PIN RECT MISMATCH expected ({0},{1}) {2}x{3}" -f $selLeft, $selTop, $selWidth, $selHeight) -ForegroundColor Yellow
        }
        if (Get-OverlayWindow) { Write-Host 'OVERLAY STILL OPEN (pin failed)' -ForegroundColor Red }
        else { Write-Host 'OVERLAY CLOSED (pin success)' }

        Invoke-Click ([int]($pin.Left + $pin.Width / 2)) ([int]($pin.Top + $pin.Height / 2))
        Send-Keys '{ESC}'
        Start-Sleep -Milliseconds 900
        Save-ScreenPng ("{0}-pin-closed.png" -f $Target)
        Show-AppWindows 'after-pin-esc'
        if (Get-PinWindow) { Write-Host 'PIN WINDOW STILL OPEN after Esc (WRONG)' -ForegroundColor Red }
        else { Write-Host 'PIN WINDOW CLOSED after Esc (ok)' }
        exit 0
    }

    'copy-busy' {
        if (-not [ZeepCutVerify]::OpenClipboard([IntPtr]::Zero)) {
            $err = [System.Runtime.InteropServices.Marshal]::GetLastWin32Error()
            Write-Host ("CLIPBOARD HOLD FAILED win32={0}" -f $err) -ForegroundColor Red
            Send-Keys '{ESC}'
            exit 5
        }
        Write-Host 'CLIPBOARD HELD by verifier'
        Send-Keys '{ENTER}'
        Start-Sleep -Milliseconds 2500
        Save-ScreenPng ("{0}-copy-busy.png" -f $Target)
        Show-AppWindows 'after-copy-busy'
        if (Get-OverlayWindow) { Write-Host 'OVERLAY STILL OPEN (expected while clipboard busy)' }
        else { Write-Host 'OVERLAY CLOSED while clipboard busy (WRONG)' -ForegroundColor Red }

        [ZeepCutVerify]::CloseClipboard() | Out-Null
        Write-Host 'CLIPBOARD RELEASED'
        Start-Sleep -Milliseconds 400
        Send-Keys '{ENTER}'
        Start-Sleep -Milliseconds 1500
        Save-ScreenPng ("{0}-copy-busy-retry.png" -f $Target)
        Show-AppWindows 'after-copy-busy-retry'
        if (Get-OverlayWindow) { Write-Host 'OVERLAY STILL OPEN after retry (copy retry failed)' -ForegroundColor Red }
        else { Write-Host 'OVERLAY CLOSED after retry (copy retry success)' }
        exit 0
    }
}
