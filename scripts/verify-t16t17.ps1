# T16/T17 desktop verification driver: annotation editing + export rendering.
#
# One process runs the whole sequence so no agent round-trip is needed while the
# overlay is open. All coordinates are physical screen pixels (PerMonitorV2).
#
# Scenarios:
#   export   select area, draw all seven annotations (incl. real-pixelated mosaic),
#            press Enter to copy, then read the clipboard from a separate process
#            and save it as PNG (this is the T17 export: no mask/border/toolbar).
#   edit     select area, draw a rectangle, back to 选择, click it (select state),
#            drag it (move), drag its bottom-right handle (resize), press Delete,
#            press Ctrl+Z (restore), Esc (deselect, overlay must stay open).
#   textedit double-click an existing text annotation, retype it, verify undo/redo
#   pin      draw rect + mosaic, then 贴图: the pin window must show the composed image
#   save     draw + 另存为 PNG through the real dialog, then verify the file (size/标注)
#   sizes    measure export size for the current screen (copy a fixed-size selection
#            and print the clipboard image size for the 125%/100% comparison).
#
# Examples:
#   -Target primary   -Scenario export
#   -Target secondary -Scenario export
#   -Target primary   -Scenario edit
#   -Target primary   -Scenario save
param(
    [ValidateSet('primary','secondary')][string]$Target = 'primary',
    [ValidateSet('export','edit','textedit','sizes','save','pin')][string]$Scenario = 'export',
    [string]$OutDir = '',
    [int]$SelX = [int]::MinValue,
    [int]$SelY = [int]::MinValue,
    [int]$SelW = 760,
    [int]$SelH = 560,
    [string]$Text = 'Zeep Cut T16'
)
$ErrorActionPreference = 'Stop'

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Text;

public static class ZeepCutT16
{
    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X; public int Y; }

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

    public delegate bool EnumChildProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool EnumChildWindows(IntPtr parent, EnumChildProc callback, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetClassName(IntPtr hWnd, StringBuilder text, int maxCount);

    [DllImport("user32.dll")]
    public static extern int GetDlgCtrlID(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    public const uint BM_CLICK = 0x00F5;

    public const uint LEFTDOWN = 0x0002;
    public const uint LEFTUP = 0x0004;

    [StructLayout(LayoutKind.Sequential)]
    public struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    // INPUT is 40 bytes on x64; Size = 40 keeps SendInput's cbSize correct.
    [StructLayout(LayoutKind.Explicit, Size = 40)]
    public struct INPUT
    {
        [FieldOffset(0)] public uint type;
        [FieldOffset(8)] public KEYBDINPUT ki;
    }

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    public const uint INPUT_KEYBOARD = 1;
    public const uint KEYEVENTF_UNICODE = 0x0004;
    public const uint KEYEVENTF_KEYUP = 0x0002;

    // SendKeys eats spaces/digits under this machine's IME, so type real Unicode keystrokes.
    public static void TypeUnicodeChar(char ch)
    {
        var inputs = new INPUT[2];
        inputs[0].type = INPUT_KEYBOARD;
        inputs[0].ki.wScan = ch;
        inputs[0].ki.dwFlags = KEYEVENTF_UNICODE;
        inputs[1].type = INPUT_KEYBOARD;
        inputs[1].ki.wScan = ch;
        inputs[1].ki.dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP;
        var sent = SendInput(2, inputs, Marshal.SizeOf(typeof(INPUT)));
        if (sent != 2)
        {
            throw new InvalidOperationException("SendInput failed, Win32Error=" + Marshal.GetLastWin32Error());
        }
    }
}
'@

[ZeepCutT16]::SetProcessDpiAwarenessContext([IntPtr](-4)) | Out-Null
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

if ([string]::IsNullOrWhiteSpace($OutDir)) { $OutDir = Join-Path $env:TEMP 'zeepcut-t16t17' }
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

if ($SelX -eq [int]::MinValue) { if ($isPrimary) { $SelX = 320 } else { $SelX = -1000 } }
if ($SelY -eq [int]::MinValue) { $SelY = 240 }
if (-not $isPrimary) { $SelW = 700; $SelH = 640 }

$selRight = $SelX + $SelW
$selBottom = $SelY + $SelH

# A static desktop window behind the selection makes the mosaic obviously "real":
# the pixelation has to average existing desktop content, not paint flat squares.
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
    for ($attempt = 0; $attempt -lt 6; $attempt++) {
        [ZeepCutT16]::SetCursorPos($x, $y) | Out-Null
        Start-Sleep -Milliseconds 90
        $p = New-Object ZeepCutT16+POINT
        [ZeepCutT16]::GetCursorPos([ref]$p) | Out-Null
        if ([Math]::Abs($p.X - $x) -le 1 -and [Math]::Abs($p.Y - $y) -le 1) { return }
        Start-Sleep -Milliseconds 180
    }
    Write-Host ("CURSOR UNREACHABLE {0},{1}" -f $x, $y) -ForegroundColor Red
}

function Invoke-Click([int]$x, [int]$y) {
    Move-Cursor $x $y
    [ZeepCutT16]::mouse_event([ZeepCutT16]::LEFTDOWN, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 90
    [ZeepCutT16]::mouse_event([ZeepCutT16]::LEFTUP, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 450
    Write-Host ("CLICK {0},{1}" -f $x, $y)
}

function Invoke-Drag([int]$x1, [int]$y1, [int]$x2, [int]$y2, [int]$steps = 18) {
    Move-Cursor $x1 $y1
    [ZeepCutT16]::mouse_event([ZeepCutT16]::LEFTDOWN, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 120
    for ($i = 1; $i -le $steps; $i++) {
        $cx = [int][Math]::Round($x1 + (($x2 - $x1) * $i / $steps))
        $cy = [int][Math]::Round($y1 + (($y2 - $y1) * $i / $steps))
        [ZeepCutT16]::SetCursorPos($cx, $cy) | Out-Null
        Start-Sleep -Milliseconds 22
    }
    Start-Sleep -Milliseconds 140
    [ZeepCutT16]::mouse_event([ZeepCutT16]::LEFTUP, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 420
    Write-Host ("DRAG {0},{1} -> {2},{3}" -f $x1, $y1, $x2, $y2)
}

function Invoke-FreehandDrag([int]$x1, [int]$y1, [int]$length, [int]$amplitude) {
    Move-Cursor $x1 $y1
    [ZeepCutT16]::mouse_event([ZeepCutT16]::LEFTDOWN, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 120
    for ($i = 1; $i -le 40; $i++) {
        $cx = $x1 + [int]($length * $i / 40)
        $cy = $y1 + [int]($amplitude * [Math]::Sin($i / 40 * 2 * [Math]::PI * 2))
        [ZeepCutT16]::SetCursorPos($cx, $cy) | Out-Null
        Start-Sleep -Milliseconds 16
    }
    Start-Sleep -Milliseconds 150
    [ZeepCutT16]::mouse_event([ZeepCutT16]::LEFTUP, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 420
    Write-Host ("FREEHAND {0},{1} len={2} amp={3}" -f $x1, $y1, $length, $amplitude)
}

function Type-Text([string]$text) {
    foreach ($ch in $text.ToCharArray()) {
        [ZeepCutT16]::TypeUnicodeChar($ch)
        Start-Sleep -Milliseconds 18
    }
    Start-Sleep -Milliseconds 220
    Write-Host ("TYPED {0}" -f $text)
}

function Send-Keys([string]$keys) {
    [System.Windows.Forms.SendKeys]::SendWait($keys)
    Start-Sleep -Milliseconds 300
    Write-Host ("SENT {0}" -f $keys)
}

function Get-OverlayRoot {
    $targetPid = (Get-Process ZeepCut -ErrorAction SilentlyContinue | Select-Object -First 1).Id
    if (-not $targetPid) { Write-Host 'UIA: ZeepCut not running' -ForegroundColor Red; return $null }
    $script:overlayHwnd = [IntPtr]::Zero
    [ZeepCutT16]::EnumWindows({ param($h, $p)
        $sb = New-Object System.Text.StringBuilder 256
        [void][ZeepCutT16]::GetWindowText($h, $sb, 256)
        $procId = 0
        [void][ZeepCutT16]::GetWindowThreadProcessId($h, [ref]$procId)
        if ($procId -eq $targetPid -and [ZeepCutT16]::IsWindowVisible($h) -and $sb.ToString().StartsWith('Zeep Cut')) {
            $script:overlayHwnd = $h
        }
        return $true
    }, [IntPtr]::Zero) | Out-Null
    $hwnd = $script:overlayHwnd
    if ($hwnd -eq [IntPtr]::Zero) { Write-Host 'UIA: overlay not found' -ForegroundColor Yellow; return $null }
    return [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)
}

function Find-Button([string]$name) {
    $root = Get-OverlayRoot
    if (-not $root) { return $null }
    foreach ($candidate in @($name, "工具$name")) {
        $cond = New-Object System.Windows.Automation.AndCondition(
            (New-Object System.Windows.Automation.PropertyCondition(
                [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
                [System.Windows.Automation.ControlType]::Button)),
            (New-Object System.Windows.Automation.PropertyCondition(
                [System.Windows.Automation.AutomationElement]::NameProperty,
                $candidate)))
        $button = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
        if ($button) {
            $r = $button.Current.BoundingRectangle
            Write-Host ("UIA BUTTON '{0}' {1},{2} {3}x{4}" -f $candidate, $r.X, $r.Y, $r.Width, $r.Height)
            return $r
        }
    }
    Write-Host ("UIA BUTTON '{0}' NOT FOUND" -f $name) -ForegroundColor Yellow
    return $null
}

function Click-Button([string]$name) {
    $r = Find-Button $name
    if (-not $r) { return $false }
    Invoke-Click ([int]($r.X + $r.Width / 2)) ([int]($r.Y + $r.Height / 2))
    return $true
}

function Select-Tool([string]$name) {
    if (-not (Click-Button "工具$name")) {
        Write-Host ("TOOL BUTTON MISSING: {0} -> abort" -f $name) -ForegroundColor Red
        exit 3
    }
}

# Reads the clipboard image from a SEPARATE process (never share clipboard state with the app).
function Save-ClipboardPng([string]$name) {
    $path = Join-Path $OutDir $name
    $script = Join-Path $PSScriptRoot 'clipboard.ps1'
    $out = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $script -Save $path 2>&1
    $out | ForEach-Object { Write-Host ("CLIP: {0}" -f $_) }
}

function Find-SaveDialogHwnd {
    $appPid = (Get-Process ZeepCut -ErrorAction SilentlyContinue | Select-Object -First 1).Id
    if (-not $appPid) { return [IntPtr]::Zero }
    $script:dlgHwnd = [IntPtr]::Zero
    [ZeepCutT16]::EnumWindows({ param($h, $p)
        $procId = 0
        [void][ZeepCutT16]::GetWindowThreadProcessId($h, [ref]$procId)
        if ($procId -ne $appPid -or -not [ZeepCutT16]::IsWindowVisible($h)) { return $true }
        $cls = New-Object System.Text.StringBuilder 256
        [void][ZeepCutT16]::GetClassName($h, $cls, 256)
        if ($cls.ToString() -eq '#32770') { $script:dlgHwnd = $h }
        return $true
    }, [IntPtr]::Zero) | Out-Null
    return $script:dlgHwnd
}

function Wait-SaveDialogHwnd([int]$timeoutMs = 6000) {
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    while ($sw.ElapsedMilliseconds -lt $timeoutMs) {
        $h = Find-SaveDialogHwnd
        if ($h -ne [IntPtr]::Zero) { Start-Sleep -Milliseconds 400; return $h }
        Start-Sleep -Milliseconds 200
    }
    return [IntPtr]::Zero
}

function Find-DialogChild([int]$controlId) {
    $hwnd = Wait-SaveDialogHwnd
    if ($hwnd -eq [IntPtr]::Zero) { return [IntPtr]::Zero }
    $script:dlgChild = [IntPtr]::Zero
    [ZeepCutT16]::EnumChildWindows($hwnd, { param($h, $p)
        if ([ZeepCutT16]::GetDlgCtrlID($h) -ne $controlId) { return $true }
        $cls = New-Object System.Text.StringBuilder 256
        [void][ZeepCutT16]::GetClassName($h, $cls, 256)
        if ($cls.ToString() -eq 'Button') { $script:dlgChild = $h }
        return $true
    }, [IntPtr]::Zero) | Out-Null
    return $script:dlgChild
}

# The Win11 dialog file-name field is DirectUI: click it, then paste the path from the
# clipboard so the Chinese IME cannot rewrite `C:\...` into `C：、...`.
function Set-DialogFileName([string]$path) {
    $hwnd = Wait-SaveDialogHwnd
    if ($hwnd -eq [IntPtr]::Zero) { Write-Host 'DIALOG NOT FOUND' -ForegroundColor Red; return $false }
    Add-Type -AssemblyName UIAutomationClient
    Add-Type -AssemblyName UIAutomationTypes
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
    if ($field) {
        Write-Host ("DIALOG FILE NOW [{0}]" -f $field.Current.Name)
        return ($field.Current.Name -eq $path)
    }
    return $false
}

function Invoke-DialogSave {
    $button = Find-DialogChild 1
    if ($button -eq [IntPtr]::Zero) { Write-Host 'DIALOG SAVE BUTTON NOT FOUND' -ForegroundColor Red; return $false }
    [ZeepCutT16]::SendMessage($button, [ZeepCutT16]::BM_CLICK, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
    Write-Host 'DIALOG SAVE CLICKED'
    return $true
}

function Wait-ForFile([string]$path, [int]$timeoutMs = 6000) {
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    while ($sw.ElapsedMilliseconds -lt $timeoutMs) {
        if (Test-Path -LiteralPath $path) { return $true }
        Start-Sleep -Milliseconds 200
    }
    return $false
}

# --- Trigger the overlay by pressing Alt+A with the pointer inside the target screen ---
Move-Cursor ([int]($rect.Left + $rect.Width / 2)) ([int]($rect.Top + $rect.Height / 2))
Send-Keys '%a'
Start-Sleep -Milliseconds 1800

Invoke-Drag $SelX $SelY $selRight $selBottom
Write-Host ("EXPECT SELECTION ({0},{1}) {2}x{3}" -f $SelX, $SelY, $SelW, $SelH)
Save-ScreenPng ("{0}-{1}-00-selection.png" -f $Target, $Scenario)

$ix = $SelX + 55
$iy = $SelY + 55

switch ($Scenario) {
    'export' {
        # Draw all seven annotation kinds inside the selection, then copy (Enter).
        Select-Tool '矩形'
        Invoke-Drag ($ix) ($iy) ($ix + 190) ($iy + 110)
        Select-Tool '椭圆'
        Invoke-Drag ($ix + 220) ($iy) ($ix + 400) ($iy + 120)
        Select-Tool '箭头'
        Invoke-Drag ($ix) ($iy + 160) ($ix + 200) ($iy + 260)
        Select-Tool '直线'
        Invoke-Drag ($ix + 230) ($iy + 160) ($ix + 400) ($iy + 240)
        Select-Tool '画笔'
        Invoke-FreehandDrag ($ix) ($iy + 320) 280 55
        Select-Tool '文字'
        Invoke-Click ($ix + 230) ($iy + 310)
        Start-Sleep -Milliseconds 350
        Type-Text $Text
        Send-Keys '{ENTER}'
        Start-Sleep -Milliseconds 400
        # Mosaic last so it is the topmost annotation. It sits on dense text content
        # (the Notepad texture block) so real pixelation is visually obvious.
        Select-Tool '马赛克'
        Invoke-Drag ($ix + 255) ($iy + 145) ($ix + 430) ($iy + 265)
        Save-ScreenPng ("{0}-export-01-all-seven.png" -f $Target)

        # Enter copies the COMPOSED image (T17), which must not contain mask/border/toolbar.
        Send-Keys '{ENTER}'
        Start-Sleep -Milliseconds 1200
        Save-ScreenPng ("{0}-export-02-after-copy.png" -f $Target)
        Save-ClipboardPng ("{0}-export-clipboard.png" -f $Target)
        Write-Host ("EXPECT EXPORT SIZE {0}x{1} (selection physical pixels)" -f $SelW, $SelH)
    }
    'save' {
        # T17 output path check: 另存为 must write the COMPOSED image too (not a plain crop).
        Select-Tool '矩形'
        Invoke-Drag ($ix) ($iy) ($ix + 200) ($iy + 120)
        Select-Tool '马赛克'
        Invoke-Drag ($ix + 240) ($iy) ($ix + 400) ($iy + 120)
        Save-ScreenPng ("{0}-save-01-drawn.png" -f $Target)

        if (-not (Click-Button '另存为')) { Write-Host 'SAVE AS BUTTON MISSING' -ForegroundColor Red; exit 3 }
        $savePath = Join-Path $OutDir ("{0}-save-as.png" -f $Target)
        if (Test-Path -LiteralPath $savePath) { Remove-Item -LiteralPath $savePath -Force }
        Start-Sleep -Milliseconds 1200
        Set-DialogFileName $savePath | Out-Null
        Invoke-DialogSave | Out-Null
        if (-not (Wait-ForFile $savePath)) { Write-Host 'SAVED FILE NOT FOUND' -ForegroundColor Red }
        else {
            Add-Type -AssemblyName System.Drawing
            $img = [System.Drawing.Image]::FromFile($savePath)
            Write-Host ("SAVED FILE {0} {1}x{2} {3} bytes" -f $savePath, $img.Width, $img.Height, (Get-Item -LiteralPath $savePath).Length)
            $img.Dispose()
        }
    }
    'textedit' {
        # T16: double-click an existing text annotation to re-edit it.
        Select-Tool '文字'
        Invoke-Click ($ix) ($iy)
        Start-Sleep -Milliseconds 350
        Type-Text 'first text'
        Send-Keys '{ENTER}'
        Start-Sleep -Milliseconds 450
        Save-ScreenPng ("{0}-textedit-01-created.png" -f $Target)

        Select-Tool '选择'
        # Double-click the text: two rapid clicks inside the app's 400 ms window
        # (Invoke-Click's 450 ms tail is too slow to register as a double click).
        Move-Cursor ($ix + 20) ($iy + 10)
        [ZeepCutT16]::mouse_event([ZeepCutT16]::LEFTDOWN, 0, 0, 0, [UIntPtr]::Zero)
        Start-Sleep -Milliseconds 40
        [ZeepCutT16]::mouse_event([ZeepCutT16]::LEFTUP, 0, 0, 0, [UIntPtr]::Zero)
        Start-Sleep -Milliseconds 120
        [ZeepCutT16]::mouse_event([ZeepCutT16]::LEFTDOWN, 0, 0, 0, [UIntPtr]::Zero)
        Start-Sleep -Milliseconds 40
        [ZeepCutT16]::mouse_event([ZeepCutT16]::LEFTUP, 0, 0, 0, [UIntPtr]::Zero)
        Start-Sleep -Milliseconds 600
        Save-ScreenPng ("{0}-textedit-02-dblclick-editor.png" -f $Target)
        Write-Host 'EXPECT TEXT EDITOR VISIBLE (double click reopened it)'

        # Replace the text: Ctrl+A then type the new content, Enter commits.
        Send-Keys '^a'
        Type-Text 'second T16 text'
        Send-Keys '{ENTER}'
        Start-Sleep -Milliseconds 500
        Save-ScreenPng ("{0}-textedit-03-committed.png" -f $Target)
        Write-Host 'EXPECT TEXT NOW "second T16 text"'

        Send-Keys '^z'
        Start-Sleep -Milliseconds 400
        Save-ScreenPng ("{0}-textedit-04-undo-text.png" -f $Target)
        Write-Host 'EXPECT TEXT BACK TO "first text" (undoable text change)'

        Send-Keys '^y'
        Start-Sleep -Milliseconds 400
        Save-ScreenPng ("{0}-textedit-05-redo-text.png" -f $Target)
    }
    'pin' {
        # T17: the pin window must show the COMPOSED image (annotations baked in).
        Select-Tool '矩形'
        Invoke-Drag ($ix) ($iy) ($ix + 200) ($iy + 120)
        Select-Tool '马赛克'
        Invoke-Drag ($ix + 240) ($iy) ($ix + 420) ($iy + 130)
        Save-ScreenPng ("{0}-pin-01-drawn.png" -f $Target)

        if (-not (Click-Button '贴图')) { Write-Host 'PIN BUTTON MISSING' -ForegroundColor Red; exit 3 }
        Start-Sleep -Milliseconds 1500
        Save-ScreenPng ("{0}-pin-02-window.png" -f $Target)
        Write-Host 'EXPECT PIN WINDOW SHOWING COMPOSED IMAGE (rect + real pixelation)'
    }
    'sizes' {
        # Minimal path for the DPI check: one rectangle so the annotations are visible,
        # then copy and report the clipboard size (must equal the selection in physical px).
        Select-Tool '矩形'
        Invoke-Drag ($ix) ($iy) ($ix + 200) ($iy + 120)
        Save-ScreenPng ("{0}-sizes-01-drawn.png" -f $Target)
        Send-Keys '{ENTER}'
        Start-Sleep -Milliseconds 1200
        Save-ClipboardPng ("{0}-sizes-clipboard.png" -f $Target)
        Write-Host ("EXPECT EXPORT SIZE {0}x{1} (selection physical pixels)" -f $SelW, $SelH)
    }
    'edit' {
        # 1) draw a rectangle to edit
        Select-Tool '矩形'
        Invoke-Drag ($ix) ($iy) ($ix + 220) ($iy + 150)
        Save-ScreenPng ("{0}-edit-01-drawn.png" -f $Target)

        # 2) back to 选择 (no drawing tool): clicking the annotation now selects it
        Select-Tool '选择'
        $cx = $ix + 110
        $cy = $iy + 75
        Invoke-Click $cx $cy
        Start-Sleep -Milliseconds 300
        Save-ScreenPng ("{0}-edit-02-selected.png" -f $Target)

        # 3) drag inside the selected annotation -> move (one history entry)
        Invoke-Drag $cx $cy ($cx + 90) ($cy + 60)
        Save-ScreenPng ("{0}-edit-03-moved.png" -f $Target)
        Write-Host 'EXPECT MOVE dx=90 dy=60 (one undo step)'

        # 4) drag the bottom-right handle -> resize
        $brx = $cx + 90 + 110
        $bry = $cy + 60 + 75
        Invoke-Drag $brx $bry ($brx + 60) ($bry + 40)
        Save-ScreenPng ("{0}-edit-04-resized.png" -f $Target)
        Write-Host 'EXPECT RESIZE +60/+40 on the bottom-right corner'

        # 5) Delete removes the selected annotation
        Send-Keys '{DELETE}'
        Start-Sleep -Milliseconds 400
        Save-ScreenPng ("{0}-edit-05-deleted.png" -f $Target)
        Write-Host 'EXPECT ANNOTATION GONE, OVERLAY STILL OPEN'

        # 6) Ctrl+Z brings it back (one step = the delete)
        Send-Keys '^z'
        Start-Sleep -Milliseconds 400
        Save-ScreenPng ("{0}-edit-06-undone.png" -f $Target)
        Write-Host 'EXPECT ANNOTATION BACK (delete undone)'

        # 7) undo the resize, then the move
        Send-Keys '^z'
        Start-Sleep -Milliseconds 400
        Save-ScreenPng ("{0}-edit-07-undo-resize.png" -f $Target)
        Send-Keys '^z'
        Start-Sleep -Milliseconds 400
        Save-ScreenPng ("{0}-edit-08-undo-move.png" -f $Target)
        Write-Host 'EXPECT RESIZE THEN MOVE UNDONE (each drag = one step)'

        # 8) re-select then Esc: Esc must only deselect, the overlay stays open
        Invoke-Click ($ix + 110) ($iy + 75)
        Start-Sleep -Milliseconds 300
        Save-ScreenPng ("{0}-edit-09-reselected.png" -f $Target)
        Send-Keys '{ESC}'
        Start-Sleep -Milliseconds 500
        Save-ScreenPng ("{0}-edit-10-esc-deselected.png" -f $Target)
        Write-Host 'EXPECT ESC ONLY DESELECTS (overlay still open, selection intact)'
    }
}

Save-ScreenPng ("{0}-{1}-99-final.png" -f $Target, $Scenario)
Send-Keys '{ESC}'
Start-Sleep -Milliseconds 900
Save-ScreenPng ("{0}-{1}-98-after-esc.png" -f $Target, $Scenario)
Write-Host 'DONE'
