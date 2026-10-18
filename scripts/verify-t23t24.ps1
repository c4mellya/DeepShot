# T23/T24 desktop verification driver: history storage + history writing.
#
# One process runs a whole scenario so no agent round-trip is needed while the overlay,
# a save dialog or a pin window is on screen. All coordinates are physical screen pixels
# (PerMonitorV2). The script does NOT read the log or the SQLite file itself: on this
# machine the Windows shell sees a stale view of %LOCALAPPDATA%\ZeepCut, so the agent
# greps the log and runs sqlite3 from WSL. The script prints pointers instead.
#
# Scenarios (all expect the app to be started FRESH so history.db is empty):
#   copy      Alt+A -> drag -> Enter (复制) -> exactly ONE row (Source=Screenshot),
#             images/*.png + thumbs/*.png exist, row size/name/time correct.
#   cancel    Alt+A -> drag -> Esc: NO new row, no new image file.
#   save      Alt+A -> drag -> 另存为 -> real Win32 dialog: exactly ONE row
#             (the old double-write bug would show 2: one for save, one for capture end).
#   pin       Alt+A -> drag -> 贴图: exactly ONE row (the pin must NOT add a second one).
#   clipboard PNG on clipboard -> Alt+S: exactly ONE row with Source=Clipboard.
#   multi     copy + pin + clipboard in one run: 3 rows total, sources 2xScreenshot+1xClipboard.
#   lock      after a copy, prove the image/thumb files are NOT held open: rename, copy
#             and delete them from this process while the app keeps running.
#   all       runs copy -> cancel -> save -> pin -> clipboard -> lock in one session and
#             prints the expected cumulative row counts after each step.
#
# Examples:
#   ... verify-t23t24.ps1 -Scenario copy
#   ... verify-t23t24.ps1 -Scenario clipboard -CursorX 900 -CursorY 600
param(
    [ValidateSet('primary','secondary')][string]$Target = 'primary',
    [ValidateSet('copy','cancel','save','pin','clipboard','multi','lock','all')][string]$Scenario = 'copy',
    [string]$OutDir = '',
    [int]$SelX = [int]::MinValue,
    [int]$SelY = [int]::MinValue,
    [int]$SelW = 640,
    [int]$SelH = 420,
    [int]$CursorX = [int]::MinValue,
    [int]$CursorY = [int]::MinValue
)
$ErrorActionPreference = 'Stop'

# The agent reads this transcript through WSL pipes; without this the Chinese button
# names come back as mojibake and the evidence cannot be checked.
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Text;

public static class ZeepCutT23
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

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    public delegate bool EnumChildProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool EnumChildWindows(IntPtr parent, EnumChildProc callback, IntPtr lParam);

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
}
'@

[ZeepCutT23]::SetProcessDpiAwarenessContext([IntPtr](-4)) | Out-Null
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

if ([string]::IsNullOrWhiteSpace($OutDir)) { $OutDir = Join-Path $env:TEMP 'zeepcut-t23t24' }
New-Item -ItemType Directory -Path $OutDir -Force | Out-Null

$screen = $null
foreach ($s in [System.Windows.Forms.Screen]::AllScreens) {
    if ($Target -eq 'primary' -and $s.Primary) { $screen = $s; break }
    if ($Target -eq 'secondary' -and -not $s.Primary) { $screen = $s; break }
}
if (-not $screen) { Write-Host ("screen not found: {0}" -f $Target) -ForegroundColor Red; exit 1 }
$rect = $screen.Bounds
if ($SelX -eq [int]::MinValue) { $SelX = if ($rect.Left -lt 0) { $rect.Left + 60 } else { 360 } }
if ($SelY -eq [int]::MinValue) { $SelY = 320 }
if ($CursorX -eq [int]::MinValue) { $CursorX = $rect.Left + [int]($rect.Width / 2) }
if ($CursorY -eq [int]::MinValue) { $CursorY = $rect.Top + [int]($rect.Height / 2) }
Write-Host ("SCREEN {0} origin=({1},{2}) {3}x{4}" -f $screen.DeviceName, $rect.Left, $rect.Top, $rect.Width, $rect.Height)

# Where the app writes history. Printed so the agent can grep it in WSL.
$dataRoot = Join-Path $env:LOCALAPPDATA 'ZeepCut'
$historyDb = Join-Path $dataRoot 'history.db'
Write-Host ("HISTORY DB {0}" -f $historyDb)
Write-Host ("HISTORY IMAGES {0}" -f (Join-Path $dataRoot 'images'))
Write-Host ("HISTORY THUMBS {0}" -f (Join-Path $dataRoot 'thumbs'))

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
        [ZeepCutT23]::SetCursorPos($x, $y) | Out-Null
        Start-Sleep -Milliseconds 80
        $p = New-Object ZeepCutT23+POINT
        [ZeepCutT23]::GetCursorPos([ref]$p) | Out-Null
        if ([Math]::Abs($p.X - $x) -le 1 -and [Math]::Abs($p.Y - $y) -le 1) { return }
        Start-Sleep -Milliseconds 150
    }
    Write-Host ("CURSOR UNREACHABLE {0},{1}" -f $x, $y) -ForegroundColor Red
}

function Invoke-Click([int]$x, [int]$y) {
    Move-Cursor $x $y
    [ZeepCutT23]::mouse_event([ZeepCutT23]::LEFTDOWN, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 90
    [ZeepCutT23]::mouse_event([ZeepCutT23]::LEFTUP, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 450
    Write-Host ("CLICK {0},{1}" -f $x, $y)
}

function Invoke-Drag([int]$x1, [int]$y1, [int]$x2, [int]$y2, [int]$steps = 18) {
    Move-Cursor $x1 $y1
    [ZeepCutT23]::mouse_event([ZeepCutT23]::LEFTDOWN, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 120
    for ($i = 1; $i -le $steps; $i++) {
        $cx = [int][Math]::Round($x1 + (($x2 - $x1) * $i / $steps))
        $cy = [int][Math]::Round($y1 + (($y2 - $y1) * $i / $steps))
        [ZeepCutT23]::SetCursorPos($cx, $cy) | Out-Null
        Start-Sleep -Milliseconds 22
    }
    Start-Sleep -Milliseconds 140
    [ZeepCutT23]::mouse_event([ZeepCutT23]::LEFTUP, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 420
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

# --- file-system probes (history.db / images / thumbs) ---
# NOTE: these read %LOCALAPPDATA% from THIS process. On this machine Windows PowerShell
# sees a stale view of some app-written files, so counts printed here are a hint only;
# the authoritative check is sqlite3 + ls from WSL. Sizes of DB/WAL are printed as a
# second signal because the DB is touched by the app, not by this shell.
function Get-HistorySnapshot {
    $snapshot = [ordered]@{
        DbExists = (Test-Path -LiteralPath $historyDb)
        DbBytes = 0
        Images = 0
        Thumbs = 0
    }
    if ($snapshot.DbExists) { $snapshot.DbBytes = (Get-Item -LiteralPath $historyDb).Length }
    $images = Join-Path $dataRoot 'images'
    $thumbs = Join-Path $dataRoot 'thumbs'
    if (Test-Path -LiteralPath $images) { $snapshot.Images = @(Get-ChildItem -LiteralPath $images -File -Filter '*.png' -ErrorAction SilentlyContinue).Count }
    if (Test-Path -LiteralPath $thumbs) { $snapshot.Thumbs = @(Get-ChildItem -LiteralPath $thumbs -File -Filter '*.png' -ErrorAction SilentlyContinue).Count }
    return $snapshot
}

function Report-History([string]$label, [int]$expectedRows, [string]$why) {
    $snapshot = Get-HistorySnapshot
    Write-Host ("HISTORY {0}: db={1} dbBytes={2} images={3} thumbs={4} 期望新记录={5}（{6}）" -f `
        $label, $snapshot.DbExists, $snapshot.DbBytes, $snapshot.Images, $snapshot.Thumbs, $expectedRows, $why)
    Write-Host ("HISTORY CHECK (run from WSL): sqlite3 'C:\Users\...\AppData\Local\ZeepCut\history.db' 'SELECT Id,Name,CreatedAtUtc,Width,Height,Source,RelativeImagePath FROM history ORDER BY CreatedAtUtc DESC LIMIT 5;'")
}


# The overlay only receives Enter/Esc when the WPF window itself has keyboard focus.
# SetForegroundWindow alone is NOT enough: GetForegroundWindow can already report the
# overlay while WPF's keyboard focus sits elsewhere (observed on this machine, and
# STATE.md already records "先点击遮罩再按 Esc"). A click inside the selection runs the
# overlay's own Activate()+Focus()+Keyboard.Focus(this) path, so always click.
function Focus-Overlay {
    $hwnd = Wait-OverlayHwnd
    if ($hwnd -eq [IntPtr]::Zero) { return $false }
    [ZeepCutT23]::SetForegroundWindow($hwnd) | Out-Null
    Start-Sleep -Milliseconds 200
    Invoke-Click ($SelX + [int]($SelW / 2)) ($SelY + [int]($SelH / 2))
    Start-Sleep -Milliseconds 300
    $fg = [ZeepCutT23]::GetForegroundWindow()
    $ok = ($fg -eq $hwnd)
    Write-Host ("OVERLAY FOCUS CLICK: foregroundMatchesOverlay={0} (0x{1:X})" -f $ok, ([IntPtr]$fg).ToInt64())
    return $ok
}

# SendKeys '{ESC}' from this shell does NOT reach the overlay on this machine (Enter does,
# which is why only Esc is posted): SendInput's VK_ESCAPE is swallowed somewhere between
# the harness and the WPF window. Posting WM_KEYDOWN/WM_KEYUP straight to the overlay hwnd
# exercises the same WPF key pipeline (PreviewKeyDown -> Cancel) and is deterministic.
function Send-EscToOverlay {
    $hwnd = Wait-OverlayHwnd
    if ($hwnd -eq [IntPtr]::Zero) { Write-Host 'ESC TARGET MISSING' -ForegroundColor Red; return $false }
    $scan = 0x01L
    $down = [IntPtr](1L -bor ($scan -shl 16))
    $up = [IntPtr](1L -bor ($scan -shl 16) -bor (1L -shl 30) -bor (1L -shl 31))
    [ZeepCutT23]::PostMessage($hwnd, 0x0100, [IntPtr]0x1B, $down) | Out-Null
    Start-Sleep -Milliseconds 70
    [ZeepCutT23]::PostMessage($hwnd, 0x0101, [IntPtr]0x1B, $up) | Out-Null
    Write-Host ("ESC POSTED to overlay 0x{0:X} (down={1} up={2})" -f ([IntPtr]$hwnd).ToInt64(), $down, $up)
    return $true
}

# --- overlay toolbar helper (same pattern as verify-t20t22.ps1) ---
function Find-OverlayHwnd {
    $appPid = Get-AppPid
    if (-not $appPid) { return [IntPtr]::Zero }
    $script:overlayHwnd = [IntPtr]::Zero
    [ZeepCutT23]::EnumWindows({ param($h, $p)
        $procId = 0
        [void][ZeepCutT23]::GetWindowThreadProcessId($h, [ref]$procId)
        if ($procId -ne $appPid -or -not [ZeepCutT23]::IsWindowVisible($h)) { return $true }
        if ([ZeepCutT23]::Title($h).StartsWith('Zeep Cut 截图遮罩')) { $script:overlayHwnd = $h }
        return $true
    }, [IntPtr]::Zero) | Out-Null
    return $script:overlayHwnd
}

function Wait-OverlayHwnd([int]$timeoutMs = 6000) {
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    while ($sw.ElapsedMilliseconds -lt $timeoutMs) {
        $h = Find-OverlayHwnd
        if ($h -ne [IntPtr]::Zero) { Start-Sleep -Milliseconds 300; return $h }
        Start-Sleep -Milliseconds 150
    }
    return [IntPtr]::Zero
}

function Click-OverlayButton([string]$name) {
    $hwnd = Wait-OverlayHwnd
    if ($hwnd -eq [IntPtr]::Zero) { Write-Host ('UIA: overlay not found for ' + $name) -ForegroundColor Yellow; return $false }
    $root = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)
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

# Alt+A -> drag the selection -> the overlay toolbar is up. Leaves the overlay OPEN.
function New-Selection {
    Move-Cursor $CursorX $CursorY
    Send-Keys '%a'
    Start-Sleep -Milliseconds 1800
    Invoke-Drag $SelX $SelY ($SelX + $SelW) ($SelY + $SelH)
    Write-Host ("EXPECT SELECTION {0},{1} {2}x{3}" -f $SelX, $SelY, $SelW, $SelH)
}

function Wait-OverlayClosed([int]$timeoutMs = 6000) {
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    while ($sw.ElapsedMilliseconds -lt $timeoutMs) {
        if ((Find-OverlayHwnd) -eq [IntPtr]::Zero) { return $true }
        Start-Sleep -Milliseconds 150
    }
    return $false
}

# --- save dialog automation (same pattern as verify-t16t17.ps1) ---
function Find-SaveDialogHwnd {
    $appPid = Get-AppPid
    if (-not $appPid) { return [IntPtr]::Zero }
    $script:dlgHwnd = [IntPtr]::Zero
    [ZeepCutT23]::EnumWindows({ param($h, $p)
        $procId = 0
        [void][ZeepCutT23]::GetWindowThreadProcessId($h, [ref]$procId)
        if ($procId -ne $appPid -or -not [ZeepCutT23]::IsWindowVisible($h)) { return $true }
        if ([ZeepCutT23]::ClassOf($h) -eq '#32770') { $script:dlgHwnd = $h }
        return $true
    }, [IntPtr]::Zero) | Out-Null
    return $script:dlgHwnd
}

function Wait-SaveDialogHwnd([int]$timeoutMs = 8000) {
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
    [ZeepCutT23]::EnumChildWindows($hwnd, { param($h, $p)
        if ([ZeepCutT23]::GetDlgCtrlID($h) -ne $controlId) { return $true }
        if ([ZeepCutT23]::ClassOf($h) -eq 'Button') { $script:dlgChild = $h }
        return $true
    }, [IntPtr]::Zero) | Out-Null
    return $script:dlgChild
}

# The Win11 dialog file-name field is DirectUI: click it, then paste the path from the
# clipboard so the Chinese IME cannot rewrite `C:\...` (T11/T16 known trap).
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
    Start-Sleep -Milliseconds 250
    Send-Keys '^a'
    Send-Keys '^v'
    Start-Sleep -Milliseconds 450
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
    [ZeepCutT23]::SendMessage($button, [ZeepCutT23]::BM_CLICK, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
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

# --- pin helpers (same pattern as verify-t20t22.ps1) ---
function Find-PinWindows {
    $appPid = Get-AppPid
    if (-not $appPid) { return @() }
    $script:pins = New-Object System.Collections.ArrayList
    [ZeepCutT23]::EnumWindows({ param($h, $p)
        $procId = 0
        [void][ZeepCutT23]::GetWindowThreadProcessId($h, [ref]$procId)
        if ($procId -ne $appPid -or -not [ZeepCutT23]::IsWindowVisible($h)) { return $true }
        if (-not [ZeepCutT23]::Title($h).StartsWith('Zeep Cut 贴图')) { return $true }
        $r = New-Object ZeepCutT23+RECT
        if (-not [ZeepCutT23]::GetWindowRect($h, [ref]$r)) { return $true }
        if (($r.Right - $r.Left) -le 0 -or ($r.Bottom - $r.Top) -le 0) { return $true }
        [void]$script:pins.Add([pscustomobject]@{
            Hwnd = $h; Left = $r.Left; Top = $r.Top
            Width = ($r.Right - $r.Left); Height = ($r.Bottom - $r.Top)
        })
        return $true
    }, [IntPtr]::Zero) | Out-Null
    return $script:pins | Sort-Object -Property @{ Expression = { ($_.Left * 10000) + $_.Top } }
}

function Close-AllPins {
    $pins = @(Find-PinWindows)
    if ($pins.Count -eq 0) { return }
    foreach ($pin in $pins) {
        Invoke-Click ([int]($pin.Left + $pin.Width - 15)) ([int]($pin.Top + 15))
        Start-Sleep -Milliseconds 600
    }
    Write-Host ("CLOSED ALL PINS: remaining={0}" -f @(Find-PinWindows).Count)
}

function Report-Pins([string]$label) {
    $pins = @(Find-PinWindows)
    Write-Host ("PINS {0}: visible count={1}" -f $label, $pins.Count)
    foreach ($pin in $pins) { Write-Host ("  " + [ZeepCutT23]::Describe([IntPtr]$pin.Hwnd)) }
    return $pins
}

# --- clipboard fixture (verification-only, WinForms allowed here) ---
function New-ClipboardSourcePng([int]$w = 300, [int]$h = 200) {
    $path = Join-Path $OutDir 'clipboard-source-t23.png'
    $bmp = New-Object System.Drawing.Bitmap($w, $h)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.Clear([System.Drawing.Color]::FromArgb(255, 32, 96, 200))
    $brush1 = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 255, 210, 40))
    $brush2 = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 240, 60, 60))
    $g.FillRectangle($brush1, 10, 10, 120, 80)
    $g.FillRectangle($brush2, 160, 100, 120, 80)
    $font = New-Object System.Drawing.Font('Segoe UI', 16)
    $white = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
    $g.DrawString('T23 CLIP', $font, $white, 60, 130)
    $g.Dispose()
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Host ("CLIPBOARD SOURCE PNG {0} {1}x{2}" -f $path, $w, $h)
    return $path
}

function Set-ClipboardImage([string]$path) {
    $bmp = [System.Drawing.Image]::FromFile($path)
    [System.Windows.Forms.Clipboard]::SetImage($bmp)
    Start-Sleep -Milliseconds 350
    $check = [System.Windows.Forms.Clipboard]::GetImage()
    if ($check) { Write-Host ("CLIPBOARD SET IMAGE {0}x{1}" -f $check.Width, $check.Height); $check.Dispose() }
    else { Write-Host 'CLIPBOARD SET IMAGE FAILED' -ForegroundColor Red }
    $script:clipboardBitmap = $bmp
}

$appPid = Get-AppPid
if (-not $appPid) { Write-Host 'ZeepCut is NOT running - start it first (scripts/run.ps1)' -ForegroundColor Red; exit 2 }
Write-Host ("APP PID {0}" -f $appPid)
Report-History 'baseline' 0 'start state (fresh database expected)'

switch ($Scenario) {
    'copy' {
        New-Selection
        Save-ScreenPng ("{0}-copy-01-selection.png" -f $Target)
        Focus-Overlay | Out-Null
        Send-Keys '{ENTER}'
        $closed = Wait-OverlayClosed
        Write-Host ("EXPECT OVERLAY CLOSED AFTER COPY: {0}" -f $closed)
        Start-Sleep -Milliseconds 800
        Save-ScreenPng ("{0}-copy-02-after.png" -f $Target)
        Report-History 'copy' 1 '复制成功后应恰好一条（Source=Screenshot）'
    }
    'cancel' {
        New-Selection
        Focus-Overlay | Out-Null
        Send-EscToOverlay | Out-Null
        $closed = Wait-OverlayClosed
        Write-Host ("EXPECT OVERLAY CLOSED AFTER ESC: {0}" -f $closed)
        Start-Sleep -Milliseconds 800
        Save-ScreenPng ("{0}-cancel-01-after.png" -f $Target)
        Report-History 'cancel' 0 '取消不记录（PLAN：取消不记录）'
    }
    'save' {
        New-Selection
        if (-not (Click-OverlayButton '另存为')) { Write-Host 'OVERLAY SAVE BUTTON MISSING' -ForegroundColor Red; exit 3 }
        $hwnd = Wait-SaveDialogHwnd
        if ($hwnd -eq [IntPtr]::Zero) { Write-Host 'SAVE DIALOG NOT FOUND' -ForegroundColor Red; exit 4 }
        Write-Host ("SAVE DIALOG " + [ZeepCutT23]::Describe($hwnd))
        Save-ScreenPng ("{0}-save-01-dialog.png" -f $Target)
        $savePath = Join-Path $OutDir ("{0}-save-as.png" -f $Target)
        if (Test-Path -LiteralPath $savePath) { Remove-Item -LiteralPath $savePath -Force }
        if (-not (Set-DialogFileName $savePath)) { Write-Host 'DIALOG FILE NAME NOT SET' -ForegroundColor Yellow }
        Save-ScreenPng ("{0}-save-02-dialog-filled.png" -f $Target)
        Invoke-DialogSave | Out-Null
        $saved = Wait-ForFile $savePath
        Write-Host ("EXPECT SAVED FILE: {0} ({1})" -f $saved, $savePath)
        $closed = Wait-OverlayClosed
        Write-Host ("EXPECT OVERLAY CLOSED AFTER SAVE: {0}" -f $closed)
        Start-Sleep -Milliseconds 900
        Report-History 'save' 1 '另存为成功只记一条（不是两条：动作一条 + 完成一条）'
    }
    'pin' {
        New-Selection
        if (-not (Click-OverlayButton '贴图')) { Write-Host 'OVERLAY PIN BUTTON MISSING' -ForegroundColor Red; exit 3 }
        Start-Sleep -Milliseconds 1500
        $pins = @(Report-Pins 'pin-after-create')
        Write-Host ("EXPECT 1 PIN: {0}" -f ($pins.Count -eq 1))
        Save-ScreenPng ("{0}-pin-01-created.png" -f $Target)
        Report-History 'pin' 1 '截图贴图只记一条（贴图来源=内部截图，不重复记）'
        Close-AllPins
    }
    'clipboard' {
        $source = New-ClipboardSourcePng 300 200
        Set-ClipboardImage $source
        Move-Cursor $CursorX $CursorY
        Send-Keys '%s'
        Start-Sleep -Milliseconds 1600
        $pins = @(Report-Pins 'clipboard-after-paste')
        Write-Host ("EXPECT 1 PIN: {0}" -f ($pins.Count -eq 1))
        Save-ScreenPng ("{0}-clipboard-01-created.png" -f $Target)
        Report-History 'clipboard' 1 '外部剪贴板贴图记一条（Source=Clipboard）'
        Close-AllPins
    }
    'multi' {
        # copy #1 (+1 row), pin #2 (+1 row, no second row), clipboard #3 (+1 row).
        New-Selection
        Focus-Overlay | Out-Null
        Send-Keys '{ENTER}'
        Wait-OverlayClosed | Out-Null
        Start-Sleep -Milliseconds 700
        Report-History 'multi-after-copy' 1 '第 1 条：复制'

        New-Selection
        Click-OverlayButton '贴图' | Out-Null
        Start-Sleep -Milliseconds 1400
        Report-History 'multi-after-pin' 1 '第 2 条：贴图（不额外记）'

        $source = New-ClipboardSourcePng 240 160
        Set-ClipboardImage $source
        Move-Cursor ($CursorX + 120) ($CursorY + 80)
        Send-Keys '%s'
        Start-Sleep -Milliseconds 1600
        Report-History 'multi-after-clipboard' 2 '第 3 条：外部剪贴板'
        $pins = @(Report-Pins 'multi-final')
        Write-Host ("EXPECT 2 PINS: {0}" -f ($pins.Count -eq 2))
        Save-ScreenPng ("{0}-multi-01-final.png" -f $Target)
        Close-AllPins
    }
    'lock' {
        New-Selection
        Focus-Overlay | Out-Null
        Send-Keys '{ENTER}'
        Wait-OverlayClosed | Out-Null
        Start-Sleep -Milliseconds 900
        Report-History 'lock' 1 '为句柄测试先记一条'

        # The newest image/thumb files must not be held open by the app: rename, copy, delete.
        $images = Join-Path $dataRoot 'images'
        $thumbs = Join-Path $dataRoot 'thumbs'
        $imageFiles = @()
        $thumbFiles = @()
        if (Test-Path -LiteralPath $images) { $imageFiles = @(Get-ChildItem -LiteralPath $images -File -Filter '*.png' | Sort-Object LastWriteTime -Descending) }
        if (Test-Path -LiteralPath $thumbs) { $thumbFiles = @(Get-ChildItem -LiteralPath $thumbs -File -Filter '*.png' | Sort-Object LastWriteTime -Descending) }
        Write-Host ("LOCK PROBE files: images={0} thumbs={1}" -f $imageFiles.Count, $thumbFiles.Count)
        if ($imageFiles.Count -eq 0) { Write-Host 'LOCK PROBE SKIPPED (no image file)' -ForegroundColor Yellow; break }

        $image = $imageFiles[0]
        $renamed = $image.FullName + '.renamed'
        try {
            Rename-Item -LiteralPath $image.FullName -NewName ($image.Name + '.renamed') -ErrorAction Stop
            Write-Host ("LOCK PROBE RENAME IMAGE: OK -> {0}.renamed" -f $image.Name)
        }
        catch {
            Write-Host ("LOCK PROBE RENAME IMAGE: FAILED {0}" -f $_.Exception.Message) -ForegroundColor Red
        }

        $copyTarget = Join-Path $OutDir ('lock-probe-' + $image.Name)
        try {
            Copy-Item -LiteralPath $renamed -Destination $copyTarget -Force -ErrorAction Stop
            $bytes = (Get-Item -LiteralPath $copyTarget).Length
            Write-Host ("LOCK PROBE COPY IMAGE: OK {0} bytes -> {1}" -f $bytes, $copyTarget)
        }
        catch {
            Write-Host ("LOCK PROBE COPY IMAGE: FAILED {0}" -f $_.Exception.Message) -ForegroundColor Red
        }

        try {
            Remove-Item -LiteralPath $renamed -Force -ErrorAction Stop
            Write-Host 'LOCK PROBE DELETE IMAGE: OK (no lock held by the app)'
        }
        catch {
            Write-Host ("LOCK PROBE DELETE IMAGE: FAILED {0}" -f $_.Exception.Message) -ForegroundColor Red
        }

        if ($thumbFiles.Count -gt 0) {
            $thumb = $thumbFiles[0]
            try {
                $thumbCopy = Join-Path $OutDir ('lock-probe-' + $thumb.Name)
                Copy-Item -LiteralPath $thumb.FullName -Destination $thumbCopy -Force -ErrorAction Stop
                Write-Host ("LOCK PROBE COPY THUMB: OK {0} bytes" -f (Get-Item -LiteralPath $thumbCopy).Length)
                Remove-Item -LiteralPath $thumbCopy -Force
            }
            catch {
                Write-Host ("LOCK PROBE THUMB: FAILED {0}" -f $_.Exception.Message) -ForegroundColor Red
            }
        }
        Write-Host 'NOTE: renaming/deleting the newest image leaves that history row pointing at a missing file (expected for this probe).'
    }
    'all' {
        New-Selection
        Focus-Overlay | Out-Null
        Send-Keys '{ENTER}'
        Wait-OverlayClosed | Out-Null
        Start-Sleep -Milliseconds 700
        Report-History 'all-after-copy' 1 '1) 复制'

        New-Selection
        Focus-Overlay | Out-Null
        Send-EscToOverlay | Out-Null
        Wait-OverlayClosed | Out-Null
        Start-Sleep -Milliseconds 600
        Report-History 'all-after-cancel' 1 '2) 取消：仍然是 1 条'

        New-Selection
        Click-OverlayButton '另存为' | Out-Null
        $hwnd = Wait-SaveDialogHwnd
        if ($hwnd -ne [IntPtr]::Zero) {
            $savePath = Join-Path $OutDir ("{0}-all-save.png" -f $Target)
            if (Test-Path -LiteralPath $savePath) { Remove-Item -LiteralPath $savePath -Force }
            Set-DialogFileName $savePath | Out-Null
            Invoke-DialogSave | Out-Null
            Wait-ForFile $savePath | Out-Null
            Wait-OverlayClosed | Out-Null
        }
        Start-Sleep -Milliseconds 800
        Report-History 'all-after-save' 2 '3) 另存为：2 条'

        New-Selection
        Click-OverlayButton '贴图' | Out-Null
        Start-Sleep -Milliseconds 1400
        Report-History 'all-after-pin' 3 '4) 贴图：3 条'

        $source = New-ClipboardSourcePng 240 160
        Set-ClipboardImage $source
        Move-Cursor ($CursorX + 120) ($CursorY + 80)
        Send-Keys '%s'
        Start-Sleep -Milliseconds 1600
        Report-History 'all-after-clipboard' 4 '5) 外部剪贴板：4 条'
        $pins = @(Report-Pins 'all-final')
        Write-Host ("EXPECT 2 PINS: {0}" -f ($pins.Count -eq 2))
        Save-ScreenPng ("{0}-all-01-final.png" -f $Target)
        Close-AllPins
    }
}
