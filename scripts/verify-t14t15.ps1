# T14/T15 desktop verification driver: annotation canvas + seven tools.
#
# One process runs the whole sequence so no agent round-trip is needed while the
# overlay is open. All coordinates are physical screen pixels (PerMonitorV2).
#
# Scenarios:
#   rect       select area, pick 矩形 tool, drag a rectangle
#   ellipse    select area, pick 椭圆 tool, drag an ellipse
#   arrow      select area, pick 箭头 tool, drag an arrow (clear head at the end point)
#   line       select area, pick 直线 tool, drag a line
#   freehand   select area, pick 画笔 tool, drag a wavy multi-point stroke
#   text       select area, pick 文字 tool, click, type text, press Enter (must NOT copy)
#   mosaic     select area, pick 马赛克 tool, drag a region
#   all        draw all seven tools in one selection (colour/thickness changes too)
#   undo-redo  draw two annotations, Ctrl+Z twice, Ctrl+Y twice
#   lock       draw one annotation, then try to move the selection (must be ignored)
#   color      pick a different colour + thickness, then draw
#
# Examples:
#   -Target primary -Scenario all
#   -Target secondary -Scenario rect
param(
    [ValidateSet('primary','secondary')][string]$Target = 'primary',
    [ValidateSet('rect','ellipse','arrow','line','freehand','text','mosaic','all','undo-redo','lock','color','buttons')]
    [string]$Scenario = 'rect',
    [string]$OutDir = '',
    [int]$SelX = [int]::MinValue,
    [int]$SelY = [int]::MinValue,
    [int]$SelW = 760,
    [int]$SelH = 560,
    [string]$Text = 'Zeep Cut T15'
)
$ErrorActionPreference = 'Stop'

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Text;

public static class ZeepCutAnno
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

    // INPUT is 40 bytes on x64 (8 header + 32-byte union); the Size = 40 keeps
    // SendInput's cbSize correct, otherwise the call is rejected and nothing types.
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

    // Types one character as a real Unicode keystroke, bypassing SendKeys parsing
    // (SendKeys eats spaces/digits under this machine's IME, which would silently
    // change the annotation text we are trying to verify).
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

[ZeepCutAnno]::SetProcessDpiAwarenessContext([IntPtr](-4)) | Out-Null
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

if ([string]::IsNullOrWhiteSpace($OutDir)) { $OutDir = Join-Path $env:TEMP 'zeepcut-t14t15' }
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
if ($SelY -eq [int]::MinValue) { $SelY = 260 }
if (-not $isPrimary) { $SelW = 700; $SelH = 620 }

$selRight = $SelX + $SelW
$selBottom = $SelY + $SelH

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
    # SetCursorPos can be dropped while the overlay is being activated, so verify
    # the pointer really moved (GetCursorPos) and retry before clicking/dragging.
    for ($attempt = 0; $attempt -lt 6; $attempt++) {
        [ZeepCutAnno]::SetCursorPos($x, $y) | Out-Null
        Start-Sleep -Milliseconds 90
        $p = New-Object ZeepCutAnno+POINT
        [ZeepCutAnno]::GetCursorPos([ref]$p) | Out-Null
        if ([Math]::Abs($p.X - $x) -le 1 -and [Math]::Abs($p.Y - $y) -le 1) { return }
        Write-Host ("CURSOR RETRY want {0},{1} got {2},{3}" -f $x, $y, $p.X, $p.Y) -ForegroundColor Yellow
        Start-Sleep -Milliseconds 180
    }
    Write-Host ("CURSOR UNREACHABLE {0},{1}" -f $x, $y) -ForegroundColor Red
}

function Invoke-Click([int]$x, [int]$y) {
    Move-Cursor $x $y
    [ZeepCutAnno]::mouse_event([ZeepCutAnno]::LEFTDOWN, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 90
    [ZeepCutAnno]::mouse_event([ZeepCutAnno]::LEFTUP, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 450
    Write-Host ("CLICK {0},{1}" -f $x, $y)
}

function Invoke-Drag([int]$x1, [int]$y1, [int]$x2, [int]$y2) {
    Move-Cursor $x1 $y1
    [ZeepCutAnno]::mouse_event([ZeepCutAnno]::LEFTDOWN, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 120
    $steps = 18
    for ($i = 1; $i -le $steps; $i++) {
        $cx = [int][Math]::Round($x1 + (($x2 - $x1) * $i / $steps))
        $cy = [int][Math]::Round($y1 + (($y2 - $y1) * $i / $steps))
        [ZeepCutAnno]::SetCursorPos($cx, $cy) | Out-Null
        Start-Sleep -Milliseconds 22
    }
    Start-Sleep -Milliseconds 140
    [ZeepCutAnno]::mouse_event([ZeepCutAnno]::LEFTUP, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 420
    Write-Host ("DRAG {0},{1} -> {2},{3}" -f $x1, $y1, $x2, $y2)
}

# Wavy freehand stroke: many small moves so the point list is clearly a curve.
function Invoke-FreehandDrag([int]$x1, [int]$y1, [int]$length, [int]$amplitude) {
    Move-Cursor $x1 $y1
    [ZeepCutAnno]::mouse_event([ZeepCutAnno]::LEFTDOWN, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 120
    for ($i = 1; $i -le 40; $i++) {
        $cx = $x1 + [int]($length * $i / 40)
        $cy = $y1 + [int]($amplitude * [Math]::Sin($i / 40 * 2 * [Math]::PI * 2))
        [ZeepCutAnno]::SetCursorPos($cx, $cy) | Out-Null
        Start-Sleep -Milliseconds 16
    }
    Start-Sleep -Milliseconds 150
    [ZeepCutAnno]::mouse_event([ZeepCutAnno]::LEFTUP, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 420
    Write-Host ("FREEHAND {0},{1} len={2} amp={3}" -f $x1, $y1, $length, $amplitude)
}

# Type literal text as real Unicode keystrokes (SendKeys drops spaces/digits here).
function Type-Text([string]$text) {
    foreach ($ch in $text.ToCharArray()) {
        [ZeepCutAnno]::TypeUnicodeChar($ch)
        Start-Sleep -Milliseconds 18
    }
    Start-Sleep -Milliseconds 220
    Write-Host ("TYPED {0}" -f $text)
}

function Send-Keys([string]$keys) {
    [System.Windows.Forms.SendKeys]::SendWait($keys)
    Start-Sleep -Milliseconds 250
    Write-Host ("SENT {0}" -f $keys)
}

function Get-OverlayRoot {
    $targetPid = (Get-Process ZeepCut -ErrorAction SilentlyContinue | Select-Object -First 1).Id
    if (-not $targetPid) { Write-Host 'UIA: ZeepCut not running' -ForegroundColor Red; return $null }
    $script:overlayHwnd = [IntPtr]::Zero
    [ZeepCutAnno]::EnumWindows({ param($h, $p)
        $sb = New-Object System.Text.StringBuilder 256
        [void][ZeepCutAnno]::GetWindowText($h, $sb, 256)
        $procId = 0
        [void][ZeepCutAnno]::GetWindowThreadProcessId($h, [ref]$procId)
        if ($procId -eq $targetPid -and [ZeepCutAnno]::IsWindowVisible($h) -and $sb.ToString().StartsWith('Zeep Cut')) {
            $script:overlayHwnd = $h
        }
        return $true
    }, [IntPtr]::Zero) | Out-Null
    $hwnd = $script:overlayHwnd
    if ($hwnd -eq [IntPtr]::Zero) { Write-Host 'UIA: overlay not found' -ForegroundColor Yellow; return $null }
    return [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)
}

# Finds a toolbar button by its AutomationProperties.Name (or visible text) and returns its physical rect.
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

# --- Trigger the overlay by pressing Alt+A with the pointer inside the target screen ---
Move-Cursor ([int]($rect.Left + $rect.Width / 2)) ([int]($rect.Top + $rect.Height / 2))
Send-Keys '%a'
Start-Sleep -Milliseconds 1800

# --- Frame the selection (no drawing tool selected yet) ---
Invoke-Drag $SelX $SelY $selRight $selBottom
Write-Host ("EXPECT SELECTION ({0},{1}) {2}x{3}" -f $SelX, $SelY, $SelW, $SelH)
Save-ScreenPng ("{0}-{1}-00-selection.png" -f $Target, $Scenario)

# Interior working area (keeps clear of the selection border).
$ix = $SelX + 60
$iy = $SelY + 60

switch ($Scenario) {
    'rect' {
        Select-Tool '矩形'
        Invoke-Drag ($ix) ($iy) ($ix + 240) ($iy + 160)
        Save-ScreenPng ("{0}-rect.png" -f $Target)
    }
    'ellipse' {
        Select-Tool '椭圆'
        Invoke-Drag ($ix) ($iy) ($ix + 260) ($iy + 180)
        Save-ScreenPng ("{0}-ellipse.png" -f $Target)
    }
    'arrow' {
        Select-Tool '箭头'
        Invoke-Drag ($ix) ($iy) ($ix + 300) ($iy + 170)
        Save-ScreenPng ("{0}-arrow.png" -f $Target)
    }
    'line' {
        Select-Tool '直线'
        Invoke-Drag ($ix) ($iy) ($ix + 300) ($iy + 120)
        Save-ScreenPng ("{0}-line.png" -f $Target)
    }
    'freehand' {
        Select-Tool '画笔'
        Invoke-FreehandDrag ($ix) ($iy + 60) 320 70
        Save-ScreenPng ("{0}-freehand.png" -f $Target)
    }
    'mosaic' {
        Select-Tool '马赛克'
        Invoke-Drag ($ix) ($iy) ($ix + 280) ($iy + 170)
        Save-ScreenPng ("{0}-mosaic.png" -f $Target)
    }
    'text' {
        Select-Tool '文字'
        # Click, type, then press Enter. Enter must confirm the text and must NOT copy/close.
        Invoke-Click ($ix) ($iy + 40)
        Start-Sleep -Milliseconds 400
        Save-ScreenPng ("{0}-text-01-editing.png" -f $Target)
        Type-Text $Text
        Save-ScreenPng ("{0}-text-02-typed.png" -f $Target)
        Send-Keys '{ENTER}'
        Start-Sleep -Milliseconds 500
        Save-ScreenPng ("{0}-text-03-confirmed.png" -f $Target)
        Write-Host 'EXPECT OVERLAY STILL OPEN AFTER ENTER (Enter must not copy)'
        # Prove the overlay is still alive: draw a second annotation on top.
        Select-Tool '矩形'
        Invoke-Drag ($ix + 40) ($iy + 140) ($ix + 240) ($iy + 260)
        Save-ScreenPng ("{0}-text-04-still-open-with-rect.png" -f $Target)
    }
    'all' {
        # 1 rectangle, 2 ellipse, 3 arrow, 4 line, 5 freehand, 6 text, 7 mosaic
        Select-Tool '矩形'
        Invoke-Drag ($ix) ($iy) ($ix + 200) ($iy + 120)
        Save-ScreenPng ("{0}-all-1-rect.png" -f $Target)

        Select-Tool '椭圆'
        Invoke-Drag ($ix + 240) ($iy) ($ix + 440) ($iy + 130)
        Save-ScreenPng ("{0}-all-2-ellipse.png" -f $Target)

        Select-Tool '箭头'
        Invoke-Drag ($ix) ($iy + 170) ($ix + 210) ($iy + 280)
        Save-ScreenPng ("{0}-all-3-arrow.png" -f $Target)

        Select-Tool '直线'
        Invoke-Drag ($ix + 250) ($iy + 170) ($ix + 440) ($iy + 250)
        Save-ScreenPng ("{0}-all-4-line.png" -f $Target)

        Select-Tool '画笔'
        Invoke-FreehandDrag ($ix) ($iy + 330) 300 60
        Save-ScreenPng ("{0}-all-5-freehand.png" -f $Target)

        Select-Tool '文字'
        Invoke-Click ($ix + 250) ($iy + 330)
        Start-Sleep -Milliseconds 350
        Type-Text $Text
        Send-Keys '{ENTER}'
        Start-Sleep -Milliseconds 400
        Save-ScreenPng ("{0}-all-6-text.png" -f $Target)

        Select-Tool '马赛克'
        Invoke-Drag ($ix + 300) ($iy + 400) ($ix + 460) ($iy + 500)
        Save-ScreenPng ("{0}-all-7-mosaic.png" -f $Target)
    }
    'undo-redo' {
        Select-Tool '矩形'
        Invoke-Drag ($ix) ($iy) ($ix + 200) ($iy + 120)
        Select-Tool '椭圆'
        Invoke-Drag ($ix + 240) ($iy) ($ix + 440) ($iy + 130)
        Save-ScreenPng ("{0}-undo-00-both.png" -f $Target)

        Send-Keys '^z'
        Save-ScreenPng ("{0}-undo-01-one-left.png" -f $Target)
        Send-Keys '^z'
        Save-ScreenPng ("{0}-undo-02-none.png" -f $Target)
        Send-Keys '^y'
        Save-ScreenPng ("{0}-undo-03-redo-one.png" -f $Target)
        Send-Keys '^y'
        Save-ScreenPng ("{0}-undo-04-redo-both.png" -f $Target)
    }
    'lock' {
        Select-Tool '矩形'
        Invoke-Drag ($ix) ($iy) ($ix + 220) ($iy + 140)
        Save-ScreenPng ("{0}-lock-01-drawn.png" -f $Target)

        # Back to "选择" (no drawing tool) so the drag below is a *selection move*
        # attempt, not a new annotation: once annotations exist the selection is
        # locked and the move must be ignored.
        Select-Tool '选择'
        $cx = [int](($SelX + $selRight) / 2)
        $cy = [int](($SelY + $selBottom) / 2)
        Invoke-Drag $cx $cy ($cx + 160) ($cy + 100)
        Save-ScreenPng ("{0}-lock-02-after-move-attempt.png" -f $Target)
        Write-Host ("EXPECT SELECTION UNCHANGED ({0},{1}) {2}x{3}" -f $SelX, $SelY, $SelW, $SelH)

        # Handles must be hidden while locked: the same corner drag must not resize.
        Invoke-Drag ($SelX + 2) ($SelY + 2) ($SelX - 60) ($SelY - 60)
        Save-ScreenPng ("{0}-lock-03-after-corner-attempt.png" -f $Target)
    }
    'buttons' {
        # Toolbar 撤销/重做 buttons (keyboard Ctrl+Z/Y is covered by undo-redo).
        Select-Tool '矩形'
        Invoke-Drag ($ix) ($iy) ($ix + 240) ($iy + 160)
        Save-ScreenPng ("{0}-buttons-01-drawn.png" -f $Target)
        if (-not (Click-Button '撤销')) { Write-Host 'UNDO BUTTON MISSING' -ForegroundColor Red; exit 3 }
        Save-ScreenPng ("{0}-buttons-02-undone.png" -f $Target)
        if (-not (Click-Button '重做')) { Write-Host 'REDO BUTTON MISSING' -ForegroundColor Red; exit 3 }
        Save-ScreenPng ("{0}-buttons-03-redone.png" -f $Target)
    }
    'color' {
        # Blue + thickness 10, then draw: proves colour/thickness apply to the next annotation.
        if (-not (Click-Button '颜色#FF0A84FF')) { Write-Host 'BLUE SWATCH MISSING' -ForegroundColor Red; exit 3 }
        if (-not (Click-Button '粗细10')) { Write-Host 'THICKNESS BUTTON MISSING' -ForegroundColor Red; exit 3 }
        Select-Tool '矩形'
        Invoke-Drag ($ix) ($iy) ($ix + 260) ($iy + 170)
        Save-ScreenPng ("{0}-color-blue-10.png" -f $Target)
    }
}

Save-ScreenPng ("{0}-{1}-99-final.png" -f $Target, $Scenario)
Send-Keys '{ESC}'
Start-Sleep -Milliseconds 900
Save-ScreenPng ("{0}-{1}-98-after-esc.png" -f $Target, $Scenario)
Write-Host 'DONE'
