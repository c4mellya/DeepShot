# T25/T26 desktop verification driver: history gallery + history management.
#
# Requirements: the app must already be running (scripts/run.ps1) with a history
# database. All coordinates are physical screen pixels (PerMonitorV2). The gallery is
# driven through UI Automation by AutomationId (history-search / history-filter-favorites /
# history-star-N / history-action-复制-N ...), the tray menu is opened by posting WM_TRAYICON
# to the app's tray message window and clicking the "历史" text band, so nothing depends on
# hard-coded pixel positions of the shell.
#
# Scenarios:
#   seed     Alt+A -> drag -> Enter (copy) twice with different selections, then a clipboard
#            pin (Alt+S). Prints the history snapshot after each step. Run this first.
#   gallery  open the gallery from the tray, screenshot it, print groups + cards from the UI
#            tree (proves date grouping + name/time/star per card).
#   search   type a keyword / uppercase keyword, assert filtering, then combine with 只看收藏,
#            then clear. Screenshots at each step.
#   favorite click star of card 0, screenshot, print star state and the star tooltip.
#   restart  close (tray exit) and relaunch the app, reopen the gallery and print the star
#            state of every card (proves the favorite was persisted).
#   rename   rename card 0 through the dialog (UIA), screenshot before/after.
#   preview  double click card 0 thumbnail, list the preview window, screenshot, close it.
#   pin      click 贴图 on card 0, count pin windows and print the history row count before
#            and after (must be unchanged).
#   delete   click 删除 on card 0, confirm the Win32 MessageBox, verify the record row AND
#            its image/thumb files are gone (files are checked from this process).
#   saveas   click 另存为 on card 0, fill the real Win32 save dialog, verify the file exists.
#   all      gallery -> search -> favorite -> rename -> preview -> pin -> delete -> saveas.
param(
    [ValidateSet('seed','gallery','search','favorite','restart','rename','preview','pin','delete','saveas','copy','empty','all')]
    [string]$Scenario = 'gallery',
    [string]$OutDir = '',
    [string]$Keyword = '084',
    [string]$NewName = 'T26 重命名验证',
    [int]$CardIndex = 0,
    [int]$TrayIndex = 3
)
$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }

Add-Type -TypeDefinition @'
using System;
using System.Text;
using System.Runtime.InteropServices;

public static class ZeepCutT25
{
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X; public int Y; }
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr value);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT point);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
    public delegate bool EnumChildProc(IntPtr parent, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr parent, EnumChildProc callback, IntPtr lParam);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr hWnd, StringBuilder text, int maxCount);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] public static extern int GetDlgCtrlID(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    public const uint BM_CLICK = 0x00F5;
    public const uint WM_TRAYICON = 0x8000 + 100;
    public const uint WM_RBUTTONUP = 0x0205;
    public const uint LEFTDOWN = 0x0002;
    public const uint LEFTUP = 0x0004;

    [StructLayout(LayoutKind.Sequential)] public struct KEYBDINPUT { public ushort wVk; public ushort wScan; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Explicit, Size = 40)] public struct INPUT { [FieldOffset(0)] public uint type; [FieldOffset(8)] public KEYBDINPUT ki; }
    [DllImport("user32.dll", SetLastError = true)] public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);
    public const uint INPUT_KEYBOARD = 1;
    public const uint KEYEVENTF_UNICODE = 0x0004;
    public const uint KEYEVENTF_KEYUP = 0x0002;

    public static void TypeText(string text)
    {
        foreach (var ch in text)
        {
            var inputs = new INPUT[2];
            inputs[0].type = INPUT_KEYBOARD; inputs[0].ki.wScan = ch; inputs[0].ki.dwFlags = KEYEVENTF_UNICODE;
            inputs[1].type = INPUT_KEYBOARD; inputs[1].ki.wScan = ch; inputs[1].ki.dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP;
            uint sent = SendInput(2, inputs, Marshal.SizeOf(typeof(INPUT)));
            if (sent != 2) { throw new InvalidOperationException("SendInput failed, Win32Error=" + Marshal.GetLastWin32Error()); }
        }
    }

    public static string Title(IntPtr hwnd) { var t = new StringBuilder(256); GetWindowText(hwnd, t, 256); return t.ToString(); }
    public static string ClassOf(IntPtr hwnd) { var c = new StringBuilder(256); GetClassName(hwnd, c, 256); return c.ToString(); }

    public static string Describe(IntPtr hwnd)
    {
        RECT r;
        if (!GetWindowRect(hwnd, out r)) { return string.Format("rect? title={0}", Title(hwnd)); }
        return string.Format("0x{0:X} {1},{2} {3}x{4} [{5}]", hwnd.ToInt64(), r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top, Title(hwnd));
    }

    public static IntPtr FindTrayWindow(uint pid)
    {
        IntPtr found = IntPtr.Zero;
        EnumWindows(delegate (IntPtr h, IntPtr l)
        {
            uint p; GetWindowThreadProcessId(h, out p);
            if (p != pid) { return true; }
            if (ClassOf(h) == "ZeepCut.TrayMessageWindow") { found = h; return false; }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    public static IntPtr FindPopupMenu()
    {
        IntPtr found = IntPtr.Zero;
        EnumWindows(delegate (IntPtr h, IntPtr l)
        {
            if (!IsWindowVisible(h)) { return true; }
            if (ClassOf(h) == "#32768") { found = h; }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    public static IntPtr FindMessageBox(uint pid)
    {
        IntPtr found = IntPtr.Zero;
        EnumWindows(delegate (IntPtr h, IntPtr l)
        {
            uint p; GetWindowThreadProcessId(h, out p);
            if (p != pid || !IsWindowVisible(h)) { return true; }
            if (ClassOf(h) == "#32770") { found = h; }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    public static IntPtr FindDialogButton(uint pid, int controlId)
    {
        IntPtr dialog = FindMessageBox(pid);
        if (dialog == IntPtr.Zero) { return IntPtr.Zero; }
        IntPtr found = IntPtr.Zero;
        EnumChildWindows(dialog, delegate (IntPtr h, IntPtr l)
        {
            if (GetDlgCtrlID(h) != controlId) { return true; }
            if (ClassOf(h) == "Button") { found = h; }
            return true;
        }, IntPtr.Zero);
        return found;
    }
}
'@

[ZeepCutT25]::SetProcessDpiAwarenessContext([IntPtr](-4)) | Out-Null
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

if ([string]::IsNullOrWhiteSpace($OutDir)) { $OutDir = Join-Path $env:TEMP 'zeepcut-t25t26' }
New-Item -ItemType Directory -Path $OutDir -Force | Out-Null
$dataRoot = Join-Path $env:LOCALAPPDATA 'ZeepCut'
$imagesDir = Join-Path $dataRoot 'images'
$thumbsDir = Join-Path $dataRoot 'thumbs'
$historyDb = Join-Path $dataRoot 'history.db'
$primary = [System.Windows.Forms.Screen]::PrimaryScreen
$screenRect = $primary.Bounds
Write-Host ("OUT DIR {0}" -f $OutDir)
Write-Host ("SCREEN {0} origin=({1},{2}) {3}x{4}" -f $primary.DeviceName, $screenRect.Left, $screenRect.Top, $screenRect.Width, $screenRect.Height)

function Get-AppPid { return (Get-Process ZeepCut -ErrorAction SilentlyContinue | Select-Object -First 1).Id }

function Save-ScreenPng([string]$name) {
    $path = Join-Path $OutDir $name
    $bmp = New-Object System.Drawing.Bitmap($screenRect.Width, $screenRect.Height)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($screenRect.Left, $screenRect.Top, 0, 0, $bmp.Size)
    $g.Dispose()
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Host ("SHOT {0}" -f $path)
}

function Move-Cursor([int]$x, [int]$y) {
    for ($attempt = 0; $attempt -lt 8; $attempt++) {
        [ZeepCutT25]::SetCursorPos($x, $y) | Out-Null
        Start-Sleep -Milliseconds 70
        $p = New-Object ZeepCutT25+POINT
        [ZeepCutT25]::GetCursorPos([ref]$p) | Out-Null
        if ([Math]::Abs($p.X - $x) -le 1 -and [Math]::Abs($p.Y - $y) -le 1) { return }
        Start-Sleep -Milliseconds 120
    }
    Write-Host ("CURSOR UNREACHABLE {0},{1}" -f $x, $y) -ForegroundColor Red
}

function Invoke-Click([int]$x, [int]$y) {
    Move-Cursor $x $y
    [ZeepCutT25]::mouse_event([ZeepCutT25]::LEFTDOWN, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 90
    [ZeepCutT25]::mouse_event([ZeepCutT25]::LEFTUP, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 400
    Write-Host ("CLICK {0},{1}" -f $x, $y)
}

function Invoke-DoubleClick([int]$x, [int]$y) {
    Move-Cursor $x $y
    for ($i = 0; $i -lt 2; $i++) {
        [ZeepCutT25]::mouse_event([ZeepCutT25]::LEFTDOWN, 0, 0, 0, [UIntPtr]::Zero)
        Start-Sleep -Milliseconds 45
        [ZeepCutT25]::mouse_event([ZeepCutT25]::LEFTUP, 0, 0, 0, [UIntPtr]::Zero)
        Start-Sleep -Milliseconds 70
    }
    Start-Sleep -Milliseconds 700
    Write-Host ("DOUBLE CLICK {0},{1}" -f $x, $y)
}

function Invoke-Drag([int]$x1, [int]$y1, [int]$x2, [int]$y2, [int]$steps = 18) {
    Move-Cursor $x1 $y1
    [ZeepCutT25]::mouse_event([ZeepCutT25]::LEFTDOWN, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 120
    for ($i = 1; $i -le $steps; $i++) {
        $cx = [int][Math]::Round($x1 + (($x2 - $x1) * $i / $steps))
        $cy = [int][Math]::Round($y1 + (($y2 - $y1) * $i / $steps))
        [ZeepCutT25]::SetCursorPos($cx, $cy) | Out-Null
        Start-Sleep -Milliseconds 22
    }
    Start-Sleep -Milliseconds 140
    [ZeepCutT25]::mouse_event([ZeepCutT25]::LEFTUP, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 500
    Write-Host ("DRAG {0},{1} -> {2},{3}" -f $x1, $y1, $x2, $y2)
}

function Send-Keys([string]$keys) {
    [System.Windows.Forms.SendKeys]::SendWait($keys)
    Start-Sleep -Milliseconds 350
    Write-Host ("SENT {0}" -f $keys)
}

# --- history files (hint only: the authoritative check is sqlite3 from WSL) ---
function Get-HistoryFiles {
    $images = @()
    $thumbs = @()
    if (Test-Path -LiteralPath $imagesDir) { $images = @(Get-ChildItem -LiteralPath $imagesDir -File -Filter '*.png' -ErrorAction SilentlyContinue) }
    if (Test-Path -LiteralPath $thumbsDir) { $thumbs = @(Get-ChildItem -LiteralPath $thumbsDir -File -Filter '*.png' -ErrorAction SilentlyContinue) }
    return [pscustomobject]@{ Images = $images.Count; Thumbs = $thumbs.Count; ImageNames = ($images | ForEach-Object { $_.Name }) }
}

function Report-Files([string]$label) {
    $f = Get-HistoryFiles
    Write-Host ("FILES {0}: images={1} thumbs={2}" -f $label, $f.Images, $f.Thumbs)
    return $f
}

# --- UIA helpers ---
function Get-AppWindow([string]$titlePrefix) {
    $pidValue = Get-AppPid
    if (-not $pidValue) { return $null }
    $cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $pidValue)
    $wins = [System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, $cond)
    foreach ($w in $wins) { if ($w.Current.Name -like ($titlePrefix + '*')) { return $w } }
    return $null
}

function Find-By([System.Windows.Automation.AutomationElement]$root, [string]$automationId) {
    $c = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, $automationId)
    return $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $c)
}

function Get-Gallery { return Get-AppWindow 'Zeep Cut 历史图库' }

# WPF owned dialogs (rename / preview) are UIA CHILDREN of the gallery window, not top-level
# children of the desktop, so search by AutomationId from the app's root element instead.
function Get-AppElement([string]$automationId) {
    $pidValue = Get-AppPid
    if (-not $pidValue) { return $null }
    $cond = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $pidValue)),
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, $automationId)))
    return [System.Windows.Automation.AutomationElement]::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
}

function Wait-AppElement([string]$automationId, [int]$timeoutMs = 8000) {
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    while ($sw.ElapsedMilliseconds -lt $timeoutMs) {
        $e = Get-AppElement $automationId
        if ($e) { Start-Sleep -Milliseconds 500; return $e }
        Start-Sleep -Milliseconds 200
    }
    return $null
}

function Wait-Gallery([int]$timeoutMs = 8000) {
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    while ($sw.ElapsedMilliseconds -lt $timeoutMs) {
        $g = Get-Gallery
        if ($g) { Start-Sleep -Milliseconds 900; return $g }
        Start-Sleep -Milliseconds 200
    }
    return $null
}

function Click-Element([System.Windows.Automation.AutomationElement]$element, [string]$label) {
    $r = $element.Current.BoundingRectangle
    Write-Host ("UIA '{0}' at {1},{2} {3}x{4}" -f $label, [int]$r.X, [int]$r.Y, [int]$r.Width, [int]$r.Height)
    Invoke-Click ([int]($r.X + $r.Width / 2)) ([int]($r.Y + $r.Height / 2))
}

function Click-GalleryButton([string]$automationId) {
    $g = Wait-Gallery
    if (-not $g) { Write-Host 'GALLERY NOT FOUND' -ForegroundColor Red; return $false }
    $e = Find-By $g $automationId
    if (-not $e) { Write-Host ("UIA ELEMENT {0} NOT FOUND" -f $automationId) -ForegroundColor Red; return $false }
    Click-Element $e $automationId
    return $true
}

# Dump what the gallery shows: group headers, cards (name / meta / star name), status line.
function Report-Gallery([string]$label) {
    $g = Get-Gallery
    if (-not $g) { Write-Host 'GALLERY NOT FOUND' -ForegroundColor Red; return }
    $all = $g.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
    $groups = New-Object System.Collections.ArrayList
    $cards = New-Object System.Collections.ArrayList
    $status = ''
    foreach ($e in $all) {
        $c = $e.Current
        if ($c.AutomationId -like 'history-group-*') { [void]$groups.Add($c.Name) }
        elseif ($c.AutomationId -like 'history-star-*') { [void]$cards.Add([pscustomobject]@{ StarId = $c.AutomationId; StarName = $c.Name }) }
        elseif ($c.AutomationId -eq 'history-status') { $status = $c.Name }
    }
    $names = @()
    $metas = @()
    foreach ($e in $all) {
        $c = $e.Current
        if ($c.AutomationId -like 'history-name-*') { $names += $c.Name }
        if ($c.AutomationId -like 'history-meta-*') { $metas += $c.Name }
    }
    Write-Host ("GALLERY {0}: status=[{1}] groups={2} cards={3}" -f $label, $status, $groups.Count, $cards.Count)
    foreach ($grp in $groups) { Write-Host ("  GROUP [{0}]" -f $grp) }
    for ($i = 0; $i -lt $cards.Count; $i++) {
        $n = if ($i -lt $names.Count) { $names[$i] } else { '?' }
        $m = if ($i -lt $metas.Count) { $metas[$i] } else { '?' }
        Write-Host ("  CARD {0}: name=[{1}] meta=[{2}] star=[{3}]" -f $i, $n, $m, $cards[$i].StarName)
    }
    $empty = Find-By $g 'history-empty-text'
    if ($empty -and $empty.Current.Name) {
        $r = $empty.Current.BoundingRectangle
        Write-Host ("  EMPTY STATE TEXT [{0}] visibleRect={1}x{2}" -f $empty.Current.Name, [int]$r.Width, [int]$r.Height)
    }
}

# --- tray menu: post the tray callback, then click the Nth text band ---
function Open-TrayMenu {
    $pidValue = Get-AppPid
    if (-not $pidValue) { Write-Host 'APP NOT RUNNING' -ForegroundColor Red; return $false }
    $tray = [ZeepCutT25]::FindTrayWindow([uint32]$pidValue)
    if ($tray -eq [IntPtr]::Zero) {
        # Right after startup the tray icon can still be registering; give it a few tries.
        for ($k = 0; $k -lt 10 -and $tray -eq [IntPtr]::Zero; $k++) {
            Start-Sleep -Milliseconds 500
            $tray = [ZeepCutT25]::FindTrayWindow([uint32]$pidValue)
        }
    }
    if ($tray -eq [IntPtr]::Zero) { Write-Host 'TRAY WINDOW NOT FOUND' -ForegroundColor Red; return $false }
    [ZeepCutT25]::PostMessage($tray, [ZeepCutT25]::WM_TRAYICON, [IntPtr][int][ZeepCutT25]::WM_RBUTTONUP, [IntPtr]::Zero) | Out-Null
    for ($i = 0; $i -lt 20; $i++) {
        Start-Sleep -Milliseconds 150
        $menu = [ZeepCutT25]::FindPopupMenu()
        if ($menu -ne [IntPtr]::Zero) { Start-Sleep -Milliseconds 400; return $true }
    }
    Write-Host 'POPUP MENU DID NOT OPEN' -ForegroundColor Red
    return $false
}

function Click-TrayBand([int]$index, [string]$shotName) {
    $menu = [ZeepCutT25]::FindPopupMenu()
    if ($menu -eq [IntPtr]::Zero) { Write-Host 'NO POPUP MENU' -ForegroundColor Red; return $false }
    $r = New-Object ZeepCutT25+RECT
    [ZeepCutT25]::GetWindowRect($menu, [ref]$r) | Out-Null
    if ($shotName) { Save-ScreenPng $shotName }
    $bmp = New-Object System.Drawing.Bitmap($screenRect.Width, $screenRect.Height)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($screenRect.Left, $screenRect.Top, 0, 0, $bmp.Size)
    $g.Dispose()
    $bands = New-Object System.Collections.ArrayList
    $start = -1
    for ($y = $r.Top; $y -lt $r.Bottom; $y++) {
        $dark = 0
        $right = [Math]::Min($r.Left + 170, $r.Right)
        for ($x = $r.Left + 10; $x -lt $right; $x++) {
            $c = $bmp.GetPixel($x, $y)
            if (($c.R + $c.G + $c.B) -lt 480) { $dark++ }
        }
        if ($dark -gt 2) { if ($start -lt 0) { $start = $y } }
        elseif ($start -ge 0) { if (($y - $start) -ge 6) { [void]$bands.Add([pscustomobject]@{ Top = $start; Bottom = $y - 1 }) }; $start = -1 }
    }
    $bmp.Dispose()
    if ($start -ge 0) { [void]$bands.Add([pscustomobject]@{ Top = $start; Bottom = $r.Bottom - 1 }) }
    Write-Host ("MENU BANDS={0} menu={1},{2} {3}x{4}" -f $bands.Count, $r.Left, $r.Top, ($r.Right - $r.Left), ($r.Bottom - $r.Top))
    if ($index -ge $bands.Count) { Write-Host 'BAND INDEX OUT OF RANGE' -ForegroundColor Red; return $false }
    $band = $bands[$index]
    $x = $r.Left + 60
    $y = [int](($band.Top + $band.Bottom) / 2)
    Write-Host ("CLICK TRAY BAND {0} at {1},{2}" -f $index, $x, $y)
    Invoke-Click $x $y
    return $true
}

function Open-HistoryGallery {
    # The tray icon lives in the Windows 11 overflow flyout; scripts/tray-menu.ps1 already
    # solves that (UIA "Show Hidden Icons" -> right-click the Zeep Cut icon -> scan menu text
    # bands). Posting WM_TRAYICON directly is NOT reliable in this session (the shell swallow
    # was observed to depend on which window owns the mouse), so use the proven helper.
    $script = Join-Path $PSScriptRoot 'tray-menu.ps1'
    # Band order in TrayIcon.BuildMenu: 0 截图 | 1 从剪贴板贴图 | 2 显示/隐藏全部贴图 | 3 历史 | 4 设置 | 5 检查更新 | 6 退出
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $script -Index $TrayIndex | ForEach-Object { Write-Host ("  [tray] " + $_) }
    $g = Wait-Gallery
    if (-not $g) { Write-Host 'GALLERY DID NOT OPEN' -ForegroundColor Red }
    return $g
}

# --- overlay helpers (same pattern as verify-t23t24.ps1) ---
function Find-Overlay {
    $pidValue = Get-AppPid
    if (-not $pidValue) { return [IntPtr]::Zero }
    $script:overlay = [IntPtr]::Zero
    [ZeepCutT25]::EnumWindows({ param($h, $p)
        $procId = 0
        [void][ZeepCutT25]::GetWindowThreadProcessId($h, [ref]$procId)
        if ($procId -ne $pidValue -or -not [ZeepCutT25]::IsWindowVisible($h)) { return $true }
        if ([ZeepCutT25]::Title($h).StartsWith('Zeep Cut 截图遮罩')) { $script:overlay = $h }
        return $true
    }, [IntPtr]::Zero) | Out-Null
    return $script:overlay
}

function Wait-Overlay([int]$timeoutMs = 8000) {
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    while ($sw.ElapsedMilliseconds -lt $timeoutMs) {
        $h = Find-Overlay
        if ($h -ne [IntPtr]::Zero) { Start-Sleep -Milliseconds 400; return $h }
        Start-Sleep -Milliseconds 150
    }
    return [IntPtr]::Zero
}

function Focus-Overlay([int]$selX, [int]$selY, [int]$selW, [int]$selH) {
    $h = Wait-Overlay
    if ($h -eq [IntPtr]::Zero) { return $false }
    [ZeepCutT25]::SetForegroundWindow($h) | Out-Null
    Start-Sleep -Milliseconds 250
    Invoke-Click ($selX + [int]($selW / 2)) ($selY + [int]($selH / 2))
    return $true
}

function Wait-OverlayClosed([int]$timeoutMs = 8000) {
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    while ($sw.ElapsedMilliseconds -lt $timeoutMs) {
        if ((Find-Overlay) -eq [IntPtr]::Zero) { return $true }
        Start-Sleep -Milliseconds 150
    }
    return $false
}

function Find-PinWindows {
    $pidValue = Get-AppPid
    if (-not $pidValue) { return @() }
    $script:pins = New-Object System.Collections.ArrayList
    [ZeepCutT25]::EnumWindows({ param($h, $p)
        $procId = 0
        [void][ZeepCutT25]::GetWindowThreadProcessId($h, [ref]$procId)
        if ($procId -ne $pidValue -or -not [ZeepCutT25]::IsWindowVisible($h)) { return $true }
        if (-not [ZeepCutT25]::Title($h).StartsWith('Zeep Cut 贴图')) { return $true }
        [void]$script:pins.Add($h)
        return $true
    }, [IntPtr]::Zero) | Out-Null
    return $script:pins
}

function New-Selection([int]$x, [int]$y, [int]$w, [int]$h) {
    Move-Cursor ($x + 40) ($y + 40)
    Send-Keys '%a'
    Start-Sleep -Milliseconds 1800
    Invoke-Drag $x $y ($x + $w) ($y + $h)
    Write-Host ("EXPECT SELECTION {0},{1} {2}x{3}" -f $x, $y, $w, $h)
}

function New-ClipboardSourcePng([int]$w = 300, [int]$h = 200) {
    $path = Join-Path $OutDir 'clipboard-source-t25.png'
    $bmp = New-Object System.Drawing.Bitmap($w, $h)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.Clear([System.Drawing.Color]::FromArgb(255, 24, 120, 90))
    $brush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 250, 220, 60))
    $g.FillRectangle($brush, 16, 16, 120, 70)
    $g.Dispose()
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return $path
}

function Set-ClipboardImage([string]$path) {
    $bmp = [System.Drawing.Image]::FromFile($path)
    [System.Windows.Forms.Clipboard]::SetImage($bmp)
    Start-Sleep -Milliseconds 350
    Write-Host ("CLIPBOARD SET {0}x{1}" -f $bmp.Width, $bmp.Height)
}

# ---------------------------------------------------------------------------------------
# Shared steps
# ---------------------------------------------------------------------------------------

function Step-SelectCard($g, [int]$index) {
    # Select the card so its action bar is visible, then report the action buttons.
    $star = Find-By $g ("history-star-{0}" -f $index)
    if (-not $star) { Write-Host 'STAR NOT FOUND' -ForegroundColor Red; return $false }
    $r = $star.Current.BoundingRectangle
    # The card itself is not a control element; click its name text to select it.
    $name = Find-By $g ("history-name-{0}" -f $index)
    $nr = $name.Current.BoundingRectangle
    Invoke-Click ([int]($nr.X + 60)) ([int]($nr.Y + $nr.Height / 2))
    return $true
}

function Report-Actions($g, [int]$index) {
    $actions = @('复制', '贴图', '改名', '删除', '另存为', '预览')
    $found = @()
    foreach ($a in $actions) {
        $e = Find-By $g ("history-action-{0}-{1}" -f $a, $index)
        if ($e) {
            $r = $e.Current.BoundingRectangle
            if ($r.Width -gt 0 -and $r.Height -gt 0) { $found += ("{0}@{1},{2}" -f $a, [int]$r.X, [int]$r.Y) }
            else { $found += ("{0}(hidden)" -f $a) }
        }
    }
    Write-Host ("ACTION BAR card {0}: {1}" -f $index, ($found -join ' | '))
}

switch ($Scenario) {
    'seed' {
        # two screenshot copies with different selections (one taller, one wider) + clipboard pin.
        $specs = @(
            @{ X = 360; Y = 300; W = 640; H = 420 },
            @{ X = 300; Y = 260; W = 760; H = 480 }
        )
        $n = 0
        foreach ($s in $specs) {
            $n++
            New-Selection $s.X $s.Y $s.W $s.H
            Save-ScreenPng ("seed-{0}-selection.png" -f $n)
            Focus-Overlay $s.X $s.Y $s.W $s.H | Out-Null
            Send-Keys '{ENTER}'
            Wait-OverlayClosed | Out-Null
            Start-Sleep -Milliseconds 900
            Report-Files ("seed-after-copy-{0}" -f $n) | Out-Null
        }
        $clip = New-ClipboardSourcePng 300 200
        Set-ClipboardImage $clip
        Move-Cursor 900 620
        Send-Keys '%s'
        Start-Sleep -Milliseconds 1600
        $pins = @(Find-PinWindows)
        Write-Host ("PINS after clipboard: {0}" -f $pins.Count)
        Save-ScreenPng 'seed-03-clipboard-pin.png'
        Report-Files 'seed-final' | Out-Null
        Write-Host 'NOTE: run sqlite3 from WSL to confirm the rows (see STATE.md).'
    }

    'gallery' {
        $g = Open-HistoryGallery
        if (-not $g) { exit 3 }
        Save-ScreenPng 'gallery-01-opened.png'
        Report-Gallery 'opened'
    }

    'search' {
        $g = Wait-Gallery; if (-not $g) { $g = Open-HistoryGallery }
        if (-not $g) { exit 3 }
        Report-Gallery 'before-search'
        $search = Find-By $g 'history-search'
        Click-Element $search 'history-search'
        [ZeepCutT25]::TypeText($Keyword)
        Start-Sleep -Milliseconds 900
        Save-ScreenPng 'search-01-keyword.png'
        Report-Gallery ("search keyword=" + $Keyword)

        # uppercase must match lowercase names (case-insensitive)
        Send-Keys '^a'
        [ZeepCutT25]::TypeText($Keyword.ToUpperInvariant())
        Start-Sleep -Milliseconds 900
        Save-ScreenPng 'search-02-uppercase.png'
        Report-Gallery ("search UPPERCASE=" + $Keyword.ToUpperInvariant())

        # nonsense keyword -> empty state
        Send-Keys '^a'
        [ZeepCutT25]::TypeText('zzz-no-such-name')
        Start-Sleep -Milliseconds 900
        Save-ScreenPng 'search-03-empty.png'
        Report-Gallery 'search no-match'

        # clear, then combine with 只看收藏
        Send-Keys '^a'
        Send-Keys '{DEL}'
        Start-Sleep -Milliseconds 800
        Report-Gallery 'search cleared'
        Click-GalleryButton 'history-filter-favorites' | Out-Null
        Start-Sleep -Milliseconds 700
        Save-ScreenPng 'search-04-favorites-only.png'
        Report-Gallery 'favorites only'
        Click-GalleryButton 'history-filter-all' | Out-Null
        Start-Sleep -Milliseconds 700
        Report-Gallery 'back to all'
    }

    'favorite' {
        $g = Wait-Gallery; if (-not $g) { $g = Open-HistoryGallery }
        if (-not $g) { exit 3 }
        Report-Gallery 'before-favorite'
        $star = Find-By $g ("history-star-{0}" -f $CardIndex)
        $before = $star.Current.Name
        Click-Element $star ("history-star-{0}" -f $CardIndex)
        Start-Sleep -Milliseconds 900
        $g = Get-Gallery
        $star = Find-By $g ("history-star-{0}" -f $CardIndex)
        Write-Host ("STAR card {0}: before=[{1}] after=[{2}]" -f $CardIndex, $before, $star.Current.Name)
        Save-ScreenPng 'favorite-01-starred.png'
        Report-Gallery 'after-favorite'
    }

    'restart' {
        # Prove persistence: exit through the tray menu, relaunch through scripts/run.ps1,
        # reopen the gallery and print every card's star state.
        & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'tray-menu.ps1') -Index 6 | ForEach-Object { Write-Host ("  [tray] " + $_) }
        Start-Sleep -Milliseconds 1800
        $alive = @(Get-Process ZeepCut -ErrorAction SilentlyContinue).Count
        Write-Host ("APP ALIVE AFTER EXIT: {0}" -f $alive)
        & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'run.ps1') | ForEach-Object { Write-Host $_ }
        Start-Sleep -Milliseconds 2500
        $g = Open-HistoryGallery
        if (-not $g) { Write-Host 'GALLERY DID NOT REOPEN' -ForegroundColor Red; exit 5 }
        Save-ScreenPng 'restart-02-gallery-after-restart.png'
        Report-Gallery 'after restart'
    }

    'rename' {
        $g = Wait-Gallery; if (-not $g) { $g = Open-HistoryGallery }
        if (-not $g) { exit 3 }
        Step-SelectCard $g $CardIndex | Out-Null
        Report-Actions $g $CardIndex
        Click-GalleryButton ("history-action-改名-{0}" -f $CardIndex) | Out-Null
        $dlg = Wait-AppElement 'history-rename-dialog'
        if (-not $dlg) { Write-Host 'RENAME DIALOG NOT FOUND' -ForegroundColor Red; exit 6 }
        Save-ScreenPng 'rename-01-dialog.png'
        $input = Find-By $dlg 'history-rename-input'
        Write-Host ("RENAME DIALOG input=[{0}]" -f $input.Current.Name)
        Click-Element $input 'history-rename-input'
        Send-Keys '^a'
        [ZeepCutT25]::TypeText($NewName)
        Start-Sleep -Milliseconds 300
        Save-ScreenPng 'rename-02-typed.png'
        $ok = Find-By $dlg 'history-rename-input'
        $buttons = $dlg.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
        $okButton = $null
        foreach ($b in $buttons) { if ($b.Current.AutomationId -eq 'history-rename-ok') { $okButton = $b } }
        if (-not $okButton) { Write-Host 'OK BUTTON NOT FOUND' -ForegroundColor Red; exit 7 }
        Click-Element $okButton '确定重命名'
        Start-Sleep -Milliseconds 1200
        $g = Get-Gallery
        Save-ScreenPng 'rename-03-after.png'
        Report-Gallery 'after-rename'
    }

    'preview' {
        $g = Wait-Gallery; if (-not $g) { $g = Open-HistoryGallery }
        if (-not $g) { exit 3 }
        $image = Find-By $g 'history-card-image-0'
        if (-not $image) {
            # images have no AutomationId; use the first ControlType.Image inside the card area
            $cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Image)
            $image = $g.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
        }
        $r = $image.Current.BoundingRectangle
        Write-Host ("THUMBNAIL {0},{1} {2}x{3}" -f [int]$r.X, [int]$r.Y, [int]$r.Width, [int]$r.Height)
        Invoke-DoubleClick ([int]($r.X + $r.Width / 2)) ([int]($r.Y + $r.Height / 2))
        Start-Sleep -Milliseconds 1200
        $p = Wait-AppElement 'history-preview-window'
        if (-not $p) { Write-Host 'PREVIEW WINDOW NOT FOUND' -ForegroundColor Red; exit 8 }
        Write-Host ("PREVIEW WINDOW [{0}] rect={1}" -f $p.Current.Name, $p.Current.BoundingRectangle)
        Save-ScreenPng 'preview-01-opened.png'
        $meta = Find-By $p 'history-preview-meta'
        Write-Host ("PREVIEW META [{0}]" -f $meta.Current.Name)
        $img = Find-By $p 'history-preview-image'
        Write-Host ("PREVIEW IMAGE rect={0}" -f $img.Current.BoundingRectangle)
        # SendKeys '{ESC}' does not reach WPF windows on this machine (STATE.md, T23); post the
        # key straight to the preview's hwnd so the window's own PreviewKeyDown runs.
        $hwnd = [IntPtr]$p.Current.NativeWindowHandle
        $down = [IntPtr](1L -bor (0x01L -shl 16))
        $up = [IntPtr](1L -bor (0x01L -shl 16) -bor (1L -shl 30) -bor (1L -shl 31))
        [ZeepCutT25]::SetForegroundWindow($hwnd) | Out-Null
        Start-Sleep -Milliseconds 200
        [ZeepCutT25]::PostMessage($hwnd, 0x0100, [IntPtr]0x1B, $down) | Out-Null
        Start-Sleep -Milliseconds 80
        [ZeepCutT25]::PostMessage($hwnd, 0x0101, [IntPtr]0x1B, $up) | Out-Null
        Write-Host ("ESC POSTED to preview 0x{0:X}" -f $hwnd.ToInt64())
        Start-Sleep -Milliseconds 900
        $p = Get-AppElement 'history-preview-window'
        Write-Host ("PREVIEW STILL OPEN: {0}" -f [bool]$p)
        Save-ScreenPng 'preview-02-closed.png'
    }

    'pin' {
        $g = Wait-Gallery; if (-not $g) { $g = Open-HistoryGallery }
        if (-not $g) { exit 3 }
        $filesBefore = Report-Files 'before-pin'
        Step-SelectCard $g $CardIndex | Out-Null
        Report-Actions $g $CardIndex
        $pinsBefore = @(Find-PinWindows).Count
        Click-GalleryButton ("history-action-贴图-{0}" -f $CardIndex) | Out-Null
        Start-Sleep -Milliseconds 1600
        $pins = @(Find-PinWindows)
        Write-Host ("PINS before={0} after={1}" -f $pinsBefore, $pins.Count)
        foreach ($h in $pins) { Write-Host ("  " + [ZeepCutT25]::Describe([IntPtr]$h)) }
        Save-ScreenPng 'pin-01-created.png'
        $filesAfter = Report-Files 'after-pin'
        Write-Host ("EXPECT PINS +1: {0}" -f ($pins.Count -eq $pinsBefore + 1))
        Write-Host ("EXPECT HISTORY UNCHANGED: {0}" -f ($filesAfter.Images -eq $filesBefore.Images -and $filesAfter.Thumbs -eq $filesBefore.Thumbs))
        # close the pin so later scenarios are not confused by it
        foreach ($h in $pins) {
            $r = New-Object ZeepCutT25+RECT
            [ZeepCutT25]::GetWindowRect([IntPtr]$h, [ref]$r) | Out-Null
            Invoke-Click ($r.Right - 15) ($r.Top + 15)
            Start-Sleep -Milliseconds 500
        }
        Write-Host ("PINS after close: {0}" -f @(Find-PinWindows).Count)
    }

    'delete' {
        $g = Wait-Gallery; if (-not $g) { $g = Open-HistoryGallery }
        if (-not $g) { exit 3 }
        $filesBefore = Report-Files 'before-delete'
        $nameEl = Find-By $g ("history-name-{0}" -f $CardIndex)
        $targetName = $nameEl.Current.Name
        Write-Host ("DELETE TARGET card {0} name=[{1}]" -f $CardIndex, $targetName)
        Step-SelectCard $g $CardIndex | Out-Null
        Report-Actions $g $CardIndex
        Click-GalleryButton ("history-action-删除-{0}" -f $CardIndex) | Out-Null
        $pidValue = Get-AppPid
        $box = [ZeepCutT25]::FindMessageBox([uint32]$pidValue)
        for ($i = 0; $i -lt 20 -and $box -eq [IntPtr]::Zero; $i++) { Start-Sleep -Milliseconds 200; $box = [ZeepCutT25]::FindMessageBox([uint32]$pidValue) }
        if ($box -eq [IntPtr]::Zero) { Write-Host 'CONFIRM BOX NOT FOUND' -ForegroundColor Red; exit 9 }
        Write-Host ("CONFIRM BOX " + [ZeepCutT25]::Describe($box))
        Save-ScreenPng 'delete-01-confirm.png'
        # 1 = IDOK on a MessageBox with OKCancel
        $ok = [ZeepCutT25]::FindDialogButton([uint32]$pidValue, 1)
        if ($ok -eq [IntPtr]::Zero) { Write-Host 'OK BUTTON NOT FOUND' -ForegroundColor Red; exit 10 }
        [ZeepCutT25]::SendMessage($ok, [ZeepCutT25]::BM_CLICK, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
        Write-Host 'CONFIRM OK CLICKED'
        Start-Sleep -Milliseconds 1500
        $filesAfter = Report-Files 'after-delete'
        Save-ScreenPng 'delete-02-after.png'
        Report-Gallery 'after-delete'
        Write-Host ("EXPECT images {0} -> {1} (one less): {2}" -f $filesBefore.Images, $filesAfter.Images, ($filesAfter.Images -eq $filesBefore.Images - 1))
        Write-Host ("EXPECT thumbs  {0} -> {1} (one less): {2}" -f $filesBefore.Thumbs, $filesAfter.Thumbs, ($filesAfter.Thumbs -eq $filesBefore.Thumbs - 1))
        Write-Host 'NOTE: run sqlite3 from WSL to confirm the row is gone.'
    }

    'saveas' {
        $g = Wait-Gallery; if (-not $g) { $g = Open-HistoryGallery }
        if (-not $g) { exit 3 }
        Step-SelectCard $g $CardIndex | Out-Null
        Report-Actions $g $CardIndex
        Click-GalleryButton ("history-action-另存为-{0}" -f $CardIndex) | Out-Null
        $pidValue = Get-AppPid
        $dlg = [ZeepCutT25]::FindMessageBox([uint32]$pidValue)
        for ($i = 0; $i -lt 40 -and $dlg -eq [IntPtr]::Zero; $i++) { Start-Sleep -Milliseconds 200; $dlg = [ZeepCutT25]::FindMessageBox([uint32]$pidValue) }
        if ($dlg -eq [IntPtr]::Zero) { Write-Host 'SAVE DIALOG NOT FOUND' -ForegroundColor Red; exit 11 }
        Write-Host ("SAVE DIALOG " + [ZeepCutT25]::Describe($dlg))
        Save-ScreenPng 'saveas-01-dialog.png'
        $savePath = Join-Path $OutDir 'saveas-history.png'
        if (Test-Path -LiteralPath $savePath) { Remove-Item -LiteralPath $savePath -Force }
        $rootDlg = [System.Windows.Automation.AutomationElement]::FromHandle($dlg)
        $fieldCond = New-Object System.Windows.Automation.AndCondition(
            (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Pane)),
            (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, '1001')),
            (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ClassNameProperty, 'Edit')))
        $field = $rootDlg.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $fieldCond)
        if ($field) {
            $fr = $field.Current.BoundingRectangle
            Invoke-Click ([int]($fr.X + $fr.Width / 2)) ([int]($fr.Y + $fr.Height / 2))
        }
        [System.Windows.Forms.Clipboard]::SetText($savePath)
        Start-Sleep -Milliseconds 250
        Send-Keys '^a'
        Send-Keys '^v'
        Start-Sleep -Milliseconds 500
        Save-ScreenPng 'saveas-02-filled.png'
        $save = [ZeepCutT25]::FindDialogButton([uint32]$pidValue, 1)
        if ($save -eq [IntPtr]::Zero) { Write-Host 'SAVE BUTTON NOT FOUND' -ForegroundColor Red; exit 12 }
        [ZeepCutT25]::SendMessage($save, [ZeepCutT25]::BM_CLICK, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
        Write-Host 'SAVE CLICKED'
        for ($i = 0; $i -lt 40 -and -not (Test-Path -LiteralPath $savePath); $i++) { Start-Sleep -Milliseconds 200 }
        if (Test-Path -LiteralPath $savePath) {
            $item = Get-Item -LiteralPath $savePath
            $bmp = [System.Drawing.Image]::FromFile($savePath)
            Write-Host ("SAVED FILE {0} bytes={1} size={2}x{3}" -f $savePath, $item.Length, $bmp.Width, $bmp.Height)
            $bmp.Dispose()
        }
        else {
            Write-Host 'SAVED FILE MISSING' -ForegroundColor Red
        }
        Start-Sleep -Milliseconds 800
        Save-ScreenPng 'saveas-03-after.png'
    }

    'copy' {
        $g = Wait-Gallery; if (-not $g) { $g = Open-HistoryGallery }
        if (-not $g) { exit 3 }
        Step-SelectCard $g $CardIndex | Out-Null
        Report-Actions $g $CardIndex
        Click-GalleryButton ("history-action-复制-{0}" -f $CardIndex) | Out-Null
        Start-Sleep -Milliseconds 800
        $img = [System.Windows.Forms.Clipboard]::GetImage()
        if ($img) {
            Write-Host ("CLIPBOARD IMAGE {0}x{1}" -f $img.Width, $img.Height)
            $img.Save((Join-Path $OutDir 'copy-clipboard.png'), [System.Drawing.Imaging.ImageFormat]::Png)
            $img.Dispose()
        }
        else { Write-Host 'CLIPBOARD EMPTY' -ForegroundColor Red }
        Save-ScreenPng 'copy-01-after.png'
        Report-Gallery 'after-copy'
    }

    'empty' {
        # Search for a name that cannot exist -> empty state with guidance, then clear it back.
        $g = Wait-Gallery; if (-not $g) { $g = Open-HistoryGallery }
        if (-not $g) { exit 3 }
        $search = Find-By $g 'history-search'
        Click-Element $search 'history-search'
        [ZeepCutT25]::TypeText('没有这条记录')
        Start-Sleep -Milliseconds 900
        Save-ScreenPng 'empty-01-no-match.png'
        Report-Gallery 'no-match'
        $clear = Find-By $g 'history-clear-filters'
        if ($clear) {
            $r = $clear.Current.BoundingRectangle
            Write-Host ("CLEAR FILTERS BUTTON {0},{1} {2}x{3}" -f [int]$r.X, [int]$r.Y, [int]$r.Width, [int]$r.Height)
            Click-Element $clear 'history-clear-filters'
        }
        else { Write-Host 'CLEAR FILTERS BUTTON MISSING' -ForegroundColor Yellow }
        Start-Sleep -Milliseconds 900
        Save-ScreenPng 'empty-02-cleared.png'
        Report-Gallery 'after-clear'
    }

    'all' {
        $script:label = 'all'
        $g = Open-HistoryGallery
        if (-not $g) { exit 3 }
        Save-ScreenPng 'all-01-gallery.png'
        Report-Gallery 'opened'

        $search = Find-By $g 'history-search'
        Click-Element $search 'history-search'
        [ZeepCutT25]::TypeText($Keyword)
        Start-Sleep -Milliseconds 900
        Save-ScreenPng 'all-02-search.png'
        Report-Gallery ("search=" + $Keyword)
        Send-Keys '^a'
        Send-Keys '{DEL}'
        Start-Sleep -Milliseconds 800
        Report-Gallery 'search cleared'
    }
}
Write-Host 'SCENARIO DONE'
