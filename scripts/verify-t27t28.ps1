# T27/T28 desktop verification driver.
#
# T27 (30 day cleanup) has no UI of its own: the authoritative checks are done from WSL
# against history.db + the images/ thumbs/ folders before and after a launch (see STATE.md).
# The part that DOES need a real desktop is the "unfavorite an expired favorite" warning,
# because it is a Win32 MessageBox owned by the gallery window.
#
# T28 (append annotation + overwrite) is fully driven here through UI Automation:
#   gallery -> select card -> 标注 (history-action-标注-N) -> editor window
#   -> pick 矩形 tool (history-edit-tool-矩形) -> drag on the image
#   -> 保存并覆盖 (history-edit-save) -> possibly confirm the one-time explanation
#   -> verify the editor closed, the gallery status line, and paste an OCR-free summary.
#
# Scenarios:
#   diagnose    print app processes, gallery visibility, card names (read only)
#   gallery     open the gallery from the tray and print groups + cards
#   unfav       click the star of the favIndex-th card twice: first to favorite/unfavorite
#               and report whether the "取消收藏" warning MessageBox appears (T27)
#   editor      open the editor for card 0 and just report what UIA sees (read only)
#   overwrite   full T28 flow: append a rectangle, save-and-overwrite, confirm dialogs
#   cancel      full T28 flow but click 取消 instead of saving (original image must not change)
#   all         gallery -> overwrite
param(
    [ValidateSet('diagnose','gallery','unfav','editor','overwrite','cancel','all')]
    [string]$Scenario = 'diagnose',
    [string]$OutDir = '',
    [int]$CardIndex = 0,
    [int]$DragX = 0,
    [int]$DragY = 0,
    [int]$DragW = 160,
    [int]$DragH = 100,
    [int]$TrayIndex = 3
)
$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }

Add-Type -TypeDefinition @'
using System;
using System.Text;
using System.Runtime.InteropServices;

public static class ZeepCutT27
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
    [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] public static extern int GetDlgCtrlID(IntPtr hWnd);

    public const uint BM_CLICK = 0x00F5;
    public const uint LEFTDOWN = 0x0002;
    public const uint LEFTUP = 0x0004;

    public static string Title(IntPtr hwnd) { var t = new StringBuilder(512); GetWindowText(hwnd, t, 512); return t.ToString(); }
    public static string ClassOf(IntPtr hwnd) { var c = new StringBuilder(256); GetClassName(hwnd, c, 256); return c.ToString(); }

    // All top-level windows of the app process, with title + rect (diagnostics).
    public static string[] ListWindows(uint pid)
    {
        var list = new System.Collections.Generic.List<string>();
        EnumWindows(delegate (IntPtr h, IntPtr l)
        {
            uint p; GetWindowThreadProcessId(h, out p);
            if (p != pid) { return true; }
            RECT r;
            GetWindowRect(h, out r);
            list.Add(string.Format("{0}|{1}|{2}|{3},{4} {5}x{6}", ClassOf(h), Title(h), IsWindowVisible(h) ? "visible" : "hidden", r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top));
            return true;
        }, IntPtr.Zero);
        return list.ToArray();
    }

    // Win32 MessageBox (#32770) of the app process; text is read through the child STATIC controls.
    public static IntPtr FindMessageBox(uint pid, string titlePrefix)
    {
        IntPtr found = IntPtr.Zero;
        EnumWindows(delegate (IntPtr h, IntPtr l)
        {
            uint p; GetWindowThreadProcessId(h, out p);
            if (p != pid || !IsWindowVisible(h)) { return true; }
            if (ClassOf(h) != "#32770") { return true; }
            if (titlePrefix.Length > 0 && !Title(h).StartsWith(titlePrefix)) { return true; }
            found = h;
            return false;
        }, IntPtr.Zero);
        return found;
    }

    public static string[] DialogStatics(IntPtr dialog)
    {
        var list = new System.Collections.Generic.List<string>();
        EnumChildWindows(dialog, delegate (IntPtr h, IntPtr l)
        {
            if (ClassOf(h) == "Static") { list.Add(Title(h)); }
            return true;
        }, IntPtr.Zero);
        return list.ToArray();
    }

    public static string[] DialogButtons(IntPtr dialog)
    {
        var list = new System.Collections.Generic.List<string>();
        EnumChildWindows(dialog, delegate (IntPtr h, IntPtr l)
        {
            if (ClassOf(h) == "Button") { list.Add(GetDlgCtrlID(h) + ":" + Title(h)); }
            return true;
        }, IntPtr.Zero);
        return list.ToArray();
    }

    [StructLayout(LayoutKind.Sequential)] public struct PtX { public int X; public int Y; }
    [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(PtX point);
    [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr hwnd);

    // Which top-level window would receive a click at (x,y)? Used to prove the driver clicks
    // the intended window instead of whatever happens to be on top (a stray capture overlay
    // triggered by Alt+A swallows clicks and looks exactly like "the button did nothing").
    public static IntPtr TopLevelAt(int x, int y)
    {
        var pt = new PtX { X = x, Y = y };
        var h = WindowFromPoint(pt);
        if (h == IntPtr.Zero) { return IntPtr.Zero; }
        return GetAncestor(h, 2 /* GA_ROOT */);
    }

    public static void Raise(IntPtr hwnd)
    {
        if (hwnd != IntPtr.Zero) { BringWindowToTop(hwnd); }
    }

    public static IntPtr FindDialogButton(IntPtr dialog, int controlId)
    {
        IntPtr found = IntPtr.Zero;
        EnumChildWindows(dialog, delegate (IntPtr h, IntPtr l)
        {
            if (GetDlgCtrlID(h) == controlId && ClassOf(h) == "Button") { found = h; }
            return true;
        }, IntPtr.Zero);
        return found;
    }
}
'@

[ZeepCutT27]::SetProcessDpiAwarenessContext([IntPtr](-4)) | Out-Null
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

if ([string]::IsNullOrWhiteSpace($OutDir)) { $OutDir = Join-Path $env:TEMP 'zeepcut-t27t28' }
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
        [ZeepCutT27]::SetCursorPos($x, $y) | Out-Null
        Start-Sleep -Milliseconds 70
        $p = New-Object ZeepCutT27+POINT
        [ZeepCutT27]::GetCursorPos([ref]$p) | Out-Null
        if ([Math]::Abs($p.X - $x) -le 1 -and [Math]::Abs($p.Y - $y) -le 1) { return }
        Start-Sleep -Milliseconds 120
    }
    Write-Host ("CURSOR UNREACHABLE {0},{1}" -f $x, $y) -ForegroundColor Red
}

# Keep a window on top of everything except dialogs of the same process: the capture overlay
# (Alt+A) is topmost and would otherwise swallow every click the driver makes.
function Raise-AppWindow([string]$titlePrefix) {
    $pidValue = Get-AppPid
    if (-not $pidValue) { return }
    [ZeepCutT27]::EnumWindows({ param($h, $l)
        $procId = 0
        [void][ZeepCutT27]::GetWindowThreadProcessId($h, [ref]$procId)
        if ($procId -ne $pidValue -or -not [ZeepCutT27]::IsWindowVisible($h)) { return $true }
        if ([ZeepCutT27]::Title($h).StartsWith($titlePrefix)) { [ZeepCutT27]::Raise($h) }
        return $true
    }, [IntPtr]::Zero) | Out-Null
    Start-Sleep -Milliseconds 250
}

function Describe-TopAt([int]$x, [int]$y) {
    $h = [ZeepCutT27]::TopLevelAt($x, $y)
    if ($h -eq [IntPtr]::Zero) { return '<none>' }
    return ("0x{0:X} [{1}]" -f $h.ToInt64(), [ZeepCutT27]::Title($h))
}

function Assert-ClickTarget([int]$x, [int]$y, [string]$expectedPrefix) {
    $t = Describe-TopAt $x $y
    if ($t -notlike ('*' + $expectedPrefix + '*')) {
        Write-Host ("CLICK TARGET MISMATCH at {0},{1}: {2} (expected a window titled '{3}*')" -f $x, $y, $t, $expectedPrefix) -ForegroundColor Red
        return $false
    }
    return $true
}

function Invoke-Click([int]$x, [int]$y) {
    Move-Cursor $x $y
    [ZeepCutT27]::mouse_event([ZeepCutT27]::LEFTDOWN, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 90
    [ZeepCutT27]::mouse_event([ZeepCutT27]::LEFTUP, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 450
    Write-Host ("CLICK {0},{1}" -f $x, $y)
}

function Invoke-Drag([int]$x1, [int]$y1, [int]$x2, [int]$y2, [int]$steps = 18) {
    Move-Cursor $x1 $y1
    [ZeepCutT27]::mouse_event([ZeepCutT27]::LEFTDOWN, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 140
    for ($i = 1; $i -le $steps; $i++) {
        $cx = [int][Math]::Round($x1 + (($x2 - $x1) * $i / $steps))
        $cy = [int][Math]::Round($y1 + (($y2 - $y1) * $i / $steps))
        [ZeepCutT27]::SetCursorPos($cx, $cy) | Out-Null
        Start-Sleep -Milliseconds 25
    }
    Start-Sleep -Milliseconds 160
    [ZeepCutT27]::mouse_event([ZeepCutT27]::LEFTUP, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 450
    Write-Host ("DRAG {0},{1} -> {2},{3}" -f $x1, $y1, $x2, $y2)
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

function Get-Gallery { return Get-AppWindow 'Zeep Cut 历史图库' }
function Get-Editor { return Get-AppWindow 'Zeep Cut 编辑历史' }

function Find-By([System.Windows.Automation.AutomationElement]$root, [string]$automationId) {
    $c = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, $automationId)
    return $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $c)
}

function Find-Content([System.Windows.Automation.AutomationElement]$root, [string]$name) {
    $c = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $name)
    return $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $c)
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

function Wait-Editor([int]$timeoutMs = 8000) {
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    while ($sw.ElapsedMilliseconds -lt $timeoutMs) {
        $e = Get-Editor
        if ($e) { Start-Sleep -Milliseconds 900; return $e }
        Start-Sleep -Milliseconds 200
    }
    return $null
}

function Click-Element([System.Windows.Automation.AutomationElement]$element, [string]$label) {
    $r = $element.Current.BoundingRectangle
    Write-Host ("UIA '{0}' at {1},{2} {3}x{4}" -f $label, [int]$r.X, [int]$r.Y, [int]$r.Width, [int]$r.Height)
    Invoke-Click ([int]($r.X + $r.Width / 2)) ([int]($r.Y + $r.Height / 2))
}

function Open-HistoryGallery {
    # Preferred: scripts/tray-menu.ps1 (UIA: overflow flyout -> right-click the Zeep Cut icon).
    # Fallback: scripts/tray-menu-direct.ps1, which posts WM_TRAYICON to the tray message
    # window. The fallback exists because the icon sometimes reports no accessible name for a
    # while after a fresh launch, and the gallery would otherwise be unreachable for the run.
    $uia = Join-Path $PSScriptRoot 'tray-menu.ps1'
    $direct = Join-Path $PSScriptRoot 'tray-menu-direct.ps1'
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $uia -Index $TrayIndex | ForEach-Object { Write-Host ("  [tray-uia] " + $_) }
    $g = Wait-Gallery 4000
    if (-not $g) {
        Write-Host '  UIA tray path did not open the gallery, falling back to posting WM_TRAYICON' -ForegroundColor Yellow
        & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $direct -Index $TrayIndex | ForEach-Object { Write-Host ("  [tray-direct] " + $_) }
        $g = Wait-Gallery 8000
    }
    if (-not $g) { Write-Host 'GALLERY DID NOT OPEN' -ForegroundColor Red }
    return $g
}

function Report-Gallery([string]$label) {
    $g = Get-Gallery
    if (-not $g) { Write-Host 'GALLERY NOT FOUND' -ForegroundColor Red; return }
    $all = $g.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
    $groups = New-Object System.Collections.ArrayList
    $cards = New-Object System.Collections.ArrayList
    $status = ''
    $names = @()
    $metas = @()
    foreach ($e in $all) {
        $c = $e.Current
        if ($c.AutomationId -like 'history-group-*') { [void]$groups.Add($c.Name) }
        elseif ($c.AutomationId -like 'history-star-*') { [void]$cards.Add([pscustomobject]@{ StarId = $c.AutomationId; StarName = $c.Name }) }
        elseif ($c.AutomationId -eq 'history-status') { $status = $c.Name }
        elseif ($c.AutomationId -like 'history-name-*') { $names += $c.Name }
        elseif ($c.AutomationId -like 'history-meta-*') { $metas += $c.Name }
    }
    Write-Host ("GALLERY {0}: status=[{1}] groups={2} cards={3}" -f $label, $status, $groups.Count, $cards.Count)
    foreach ($grp in $groups) { Write-Host ("  GROUP [{0}]" -f $grp) }
    for ($i = 0; $i -lt $cards.Count; $i++) {
        $n = if ($i -lt $names.Count) { $names[$i] } else { '?' }
        $m = if ($i -lt $metas.Count) { $metas[$i] } else { '?' }
        Write-Host ("  CARD {0}: name=[{1}] meta=[{2}] star=[{3}]" -f $i, $n, $m, $cards[$i].StarName)
    }
    return $status
}

function Report-Editor([string]$label) {
    $e = Get-Editor
    if (-not $e) { Write-Host 'EDITOR NOT FOUND' -ForegroundColor Red; return $null }
    $all = $e.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
    $info = ''
    $status = ''
    $tools = @()
    foreach ($x in $all) {
        $c = $x.Current
        if ($c.AutomationId -eq 'history-edit-record-info') { $info = $c.Name }
        if ($c.AutomationId -eq 'history-edit-status') { $status = $c.Name }
        if ($c.AutomationId -like 'history-edit-tool-*') { $tools += $c.AutomationId }
    }
    $r = $e.Current.BoundingRectangle
    Write-Host ("EDITOR {0}: title=[{1}] rect={2},{3} {4}x{5}" -f $label, $e.Current.Name, [int]$r.X, [int]$r.Y, [int]$r.Width, [int]$r.Height)
    Write-Host ("  INFO [{0}]" -f $info)
    Write-Host ("  STATUS [{0}]" -f $status)
    Write-Host ("  TOOLS {0}" -f ($tools -join ','))
    return $info
}

# Win32 app MessageBox (owned by the gallery OR the editor): click a standard button.
function Get-AppDialog([string]$titlePrefix = '') {
    $pidValue = Get-AppPid
    if (-not $pidValue) { return [IntPtr]::Zero }
    return [ZeepCutT27]::FindMessageBox([uint32]$pidValue, $titlePrefix)
}

function Wait-AppDialog([string]$titlePrefix = '', [int]$timeoutMs = 4000) {
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    while ($sw.ElapsedMilliseconds -lt $timeoutMs) {
        $d = Get-AppDialog $titlePrefix
        if ($d -ne [IntPtr]::Zero) { Start-Sleep -Milliseconds 300; return $d }
        Start-Sleep -Milliseconds 150
    }
    return [IntPtr]::Zero
}

function Report-Dialog([IntPtr]$dialog, [string]$label, [string]$shotName) {
    $title = [ZeepCutT27]::Title($dialog)
    $texts = [ZeepCutT27]::DialogStatics($dialog)
    $buttons = [ZeepCutT27]::DialogButtons($dialog)
    Write-Host ("DIALOG {0}: title=[{1}]" -f $label, $title)
    foreach ($t in $texts) { Write-Host ("  TEXT [{0}]" -f $t) }
    Write-Host ("  BUTTONS [{0}]" -f ($buttons -join ' | '))
    if ($shotName) { Save-ScreenPng $shotName }
}

function Click-DialogButton([IntPtr]$dialog, [int]$controlId, [string]$label) {
    $b = [ZeepCutT27]::FindDialogButton($dialog, $controlId)
    if ($b -eq [IntPtr]::Zero) { Write-Host ("DIALOG BUTTON {0} NOT FOUND" -f $controlId) -ForegroundColor Red; return $false }
    [ZeepCutT27]::SendMessage($b, [ZeepCutT27]::BM_CLICK, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
    Start-Sleep -Milliseconds 500
    Write-Host ("DIALOG CLICK {0} ({1})" -f $label, $controlId)
    return $true
}

function Select-Card($g, [int]$index) {
    $name = Find-By $g ("history-name-{0}" -f $index)
    if (-not $name) { Write-Host 'CARD NAME NOT FOUND' -ForegroundColor Red; return $false }
    # Click the *thumbnail* area: the card's action bar is only built/laid out for the
    # selected card, and clicking the name row after a Reload used to miss.
    $thumb = Find-By $g ("history-star-{0}" -f $index)
    $target = $name.Current.BoundingRectangle
    if ($thumb) {
        $tr = $thumb.Current.BoundingRectangle
        $target = New-Object System.Windows.Rect(($tr.X + 40), ($tr.Y + 40), 60, 60)
    }
    Invoke-Click ([int]($target.X + $target.Width / 2)) ([int]($target.Y + $target.Height / 2))

    # Wait for the action bar of THIS card (only the selected card exposes it).
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    while ($sw.ElapsedMilliseconds -lt 3000) {
        if (Find-By $g ("history-action-标注-{0}" -f $index)) { return $true }
        Start-Sleep -Milliseconds 200
    }
    Write-Host ("CARD {0} SELECTED BUT ACTION BAR NOT VISIBLE" -f $index) -ForegroundColor Red
    return $true
}

function Report-RecordInfo([string]$label) {
    $g = Get-Gallery
    if (-not $g) { return }
    $all = $g.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
    $names = @(); $metas = @()
    foreach ($e in $all) {
        $c = $e.Current
        if ($c.AutomationId -like 'history-name-*') { $names += $c.Name }
        elseif ($c.AutomationId -like 'history-meta-*') { $metas += $c.Name }
    }
    Write-Host ("RECORDS {0}: count={1}" -f $label, $names.Count)
    for ($i = 0; $i -lt $names.Count; $i++) { Write-Host ("  R{0} name=[{1}] meta=[{2}]" -f $i, $names[$i], $metas[$i]) }
}

function Report-Files([string]$label) {
    $images = @(Get-ChildItem -LiteralPath $imagesDir -File -Filter '*.png' -ErrorAction SilentlyContinue)
    $thumbs = @(Get-ChildItem -LiteralPath $thumbsDir -File -Filter '*.png' -ErrorAction SilentlyContinue)
    Write-Host ("FILES {0}: images={1} thumbs={2}" -f $label, $images.Count, $thumbs.Count)
    return [pscustomobject]@{ Images = $images.Count; Thumbs = $thumbs.Count; ImageNames = ($images | ForEach-Object { $_.Name }) }
}

# ---------------------------------------------------------------------------------------
# Scenarios
# ---------------------------------------------------------------------------------------

function Report-TopWindows {
    foreach ($pt in @(@(790, 900), @(766, 887), @(720, 816), @(1290, 300))) {
        Write-Host ("TOP AT {0},{1}: {2}" -f $pt[0], $pt[1], (Describe-TopAt $pt[0] $pt[1]))
    }
}

function Step-Diagnose {
    Report-TopWindows
    $pidValue = Get-AppPid
    if (-not $pidValue) { Write-Host 'APP NOT RUNNING' -ForegroundColor Red; return }
    Write-Host ("APP PID {0}" -f $pidValue)
    foreach ($line in [ZeepCutT27]::ListWindows([uint32]$pidValue)) { Write-Host ("  WIN {0}" -f $line) }
    $g = Get-Gallery
    Write-Host ("GALLERY VISIBLE {0}" -f ($null -ne $g))
    if ($g) { Report-Gallery 'diagnose' | Out-Null }
    $e = Get-Editor
    Write-Host ("EDITOR VISIBLE {0}" -f ($null -ne $e))
    if ($e) { Report-Editor 'diagnose' | Out-Null }
}

function Step-Gallery {
    $g = Wait-Gallery 2000
    if (-not $g) { $g = Open-HistoryGallery }
    if (-not $g) { return }
    Start-Sleep -Milliseconds 600
    Save-ScreenPng 'gallery.png'
    Report-Gallery 'open' | Out-Null
}

# T27: unfavorite an expired favorite -> warning MessageBox -> cancel keeps the favorite.
function Step-Unfavorite([int]$index) {
    $g = Wait-Gallery 2000
    if (-not $g) { $g = Open-HistoryGallery }
    if (-not $g) { return }
    Start-Sleep -Milliseconds 600
    Report-Gallery 'before-unfav' | Out-Null

    $star = Find-By $g ("history-star-{0}" -f $index)
    if (-not $star) { Write-Host 'STAR NOT FOUND' -ForegroundColor Red; return }
    Write-Host ("STAR {0} name=[{1}]" -f $star.Current.AutomationId, $star.Current.Name)
    Click-Element $star ("history-star-{0}" -f $index)

    $d = Wait-AppDialog '取消收藏' 5000
    if ($d -eq [IntPtr]::Zero) {
        Write-Host 'NO WARNING DIALOG APPEARED' -ForegroundColor Red
        Report-Gallery 'after-unfav-nodialog' | Out-Null
        return
    }

    Report-Dialog $d 'unfavorite-expired' 'unfav-warning.png'
    # IDCANCEL = 2 -> keep the favorite
    Click-DialogButton $d 2 '取消'
    Start-Sleep -Milliseconds 700
    Report-Gallery 'after-unfav-cancel' | Out-Null
    Save-ScreenPng 'after-unfav-cancel.png'
}

function Step-OpenEditor([int]$index) {
    $g = Wait-Gallery 2000
    if (-not $g) { $g = Open-HistoryGallery }
    if (-not $g) { return $null }
    Start-Sleep -Milliseconds 600
    Raise-AppWindow 'Zeep Cut 历史图库'
    if (-not (Select-Card $g $index)) { return $null }
    $button = Find-By $g ("history-action-标注-{0}" -f $index)
    if (-not $button) {
        Write-Host ("ACTION 标注 NOT FOUND for card {0}" -f $index) -ForegroundColor Red
        return $null
    }
    Raise-AppWindow 'Zeep Cut 历史图库'
    Click-Element $button '标注'
    $e = Wait-Editor
    if (-not $e) { Write-Host 'EDITOR DID NOT OPEN' -ForegroundColor Red; return $null }
    return $e
}

# Rectangle tool + one drag on the image. Returns the image rectangle used (screen coords).
function Step-DrawRectangle($e, [int]$x, [int]$y, [int]$w, [int]$h) {
    $tool = Find-By $e 'history-edit-tool-矩形'
    if (-not $tool) { Write-Host 'TOOL 矩形 NOT FOUND' -ForegroundColor Red; return $null }
    Click-Element $tool 'history-edit-tool-矩形'
    $img = Find-By $e 'history-edit-image'
    if (-not $img) { Write-Host 'EDIT IMAGE NOT FOUND' -ForegroundColor Red; return $null }
    $r = $img.Current.BoundingRectangle
    Write-Host ("IMAGE RECT {0},{1} {2}x{3}" -f [int]$r.X, [int]$r.Y, [int]$r.Width, [int]$r.Height)
    if ($x -eq 0 -and $y -eq 0) { $x = [int]$r.X + 40; $y = [int]$r.Y + 40 }
    if ($x -lt $r.X -or $y -lt $r.Y) { Write-Host 'DRAG START OUTSIDE IMAGE' -ForegroundColor Red; return $null }
    Invoke-Drag $x $y ($x + $w) ($y + $h)
    return [pscustomobject]@{ X = $x; Y = $y; W = $w; H = $h; Image = $r }
}

function Step-SaveOverwrite($e, [string]$shotName) {
    $save = Find-By $e 'history-edit-save'
    if (-not $save) { Write-Host 'SAVE BUTTON NOT FOUND' -ForegroundColor Red; return $false }
    Write-Host ("SAVE BUTTON name=[{0}]" -f $save.Current.Name)
    Click-Element $save 'history-edit-save'

    # The one-time explanation is a MessageBox owned by the editor ("保存并覆盖").
    $d = Wait-AppDialog '保存并覆盖' 4000
    if ($d -ne [IntPtr]::Zero) {
        Report-Dialog $d 'overwrite-explanation' $shotName
        Click-DialogButton $d 1 '确定'
    } else {
        Write-Host 'NO OVERWRITE EXPLANATION DIALOG (already explained before, or none this run)'
    }

    # Wait for the editor to close.
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    while ($sw.ElapsedMilliseconds -lt 8000) {
        if (-not (Get-Editor)) { return $true }
        Start-Sleep -Milliseconds 200
    }
    return $false
}

function Step-Overwrite([int]$index) {
    $e = Step-OpenEditor $index
    if (-not $e) { return }
    Report-Editor 'opened' | Out-Null
    Save-ScreenPng 'editor-opened.png'

    $drag = Step-DrawRectangle $e $DragX $DragY $DragW $DragH
    if (-not $drag) { return }
    Save-ScreenPng 'editor-after-draw.png'

    # Ctrl+Z then Ctrl+Y exercises the undo stack inside the editor without touching the file.
    Start-Sleep -Milliseconds 300
    if (-not (Step-SaveOverwrite $e 'overwrite-explanation.png')) {
        Write-Host 'EDITOR STILL OPEN AFTER SAVE' -ForegroundColor Red
        Report-Editor 'after-save' | Out-Null
        return
    }

    Start-Sleep -Milliseconds 900
    Save-ScreenPng 'gallery-after-overwrite.png'
    $status = Report-Gallery 'after-overwrite'
    Report-RecordInfo 'after-overwrite'
    Report-Files 'after-overwrite' | Out-Null
    Write-Host ("SAVED drag={0},{1} {2}x{3}" -f $drag.X, $drag.Y, $drag.W, $drag.H)
}

function Step-Cancel([int]$index) {
    $e = Step-OpenEditor $index
    if (-not $e) { return }
    Report-Editor 'opened' | Out-Null
    $drag = Step-DrawRectangle $e $DragX $DragY $DragW $DragH
    if (-not $drag) { return }
    Save-ScreenPng 'editor-drawn-before-cancel.png'

    $cancel = Find-By $e 'history-edit-cancel'
    if (-not $cancel) { Write-Host 'CANCEL BUTTON NOT FOUND' -ForegroundColor Red; return }
    Click-Element $cancel 'history-edit-cancel'

    $d = Wait-AppDialog '取消编辑' 4000
    if ($d -ne [IntPtr]::Zero) {
        Report-Dialog $d 'cancel-confirm' 'cancel-confirm.png'
        Click-DialogButton $d 1 '确定'   # OK = discard
    } else {
        Write-Host 'NO CANCEL CONFIRM DIALOG'
    }

    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    while ($sw.ElapsedMilliseconds -lt 8000) {
        if (-not (Get-Editor)) { break }
        Start-Sleep -Milliseconds 200
    }

    Start-Sleep -Milliseconds 700
    Report-Gallery 'after-cancel' | Out-Null
    Report-RecordInfo 'after-cancel'
    Report-Files 'after-cancel' | Out-Null
}

# ---------------------------------------------------------------------------------------

switch ($Scenario) {
    'diagnose'  { Step-Diagnose }
    'gallery'   { Step-Gallery }
    'unfav'     { Step-Unfavorite $CardIndex }
    'editor'    {
        $e = Step-OpenEditor $CardIndex
        if ($e) { Report-Editor 'opened' | Out-Null; Save-ScreenPng 'editor-opened.png' }
    }
    'overwrite' { Step-Overwrite $CardIndex }
    'cancel'    { Step-Cancel $CardIndex }
    'all'       {
        Step-Gallery
        Step-Overwrite $CardIndex
    }
}
Write-Host 'DONE'
