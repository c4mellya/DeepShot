# Open the Zeep Cut tray menu and click one of its items (verification helper).
#
# Why this exists:
#   - On Windows 11 the icon usually sits in the "Show Hidden Icons" overflow flyout, so a
#     hard-coded click at the shell-reported rect misses it. This script opens the flyout
#     through UI Automation ("Show Hidden Icons" button), then right-clicks the "Zeep Cut"
#     icon inside it.
#   - The popup menu is a plain Win32 menu (#32768): UI Automation reports ZERO items for
#     it, so items cannot be found by name. Instead the menu bitmap is scanned for text
#     rows and the Nth text band is clicked. Item order is fixed in TrayIcon.BuildMenu:
#       0 截图 | 1 从剪贴板贴图 | 2 显示/隐藏全部贴图 | 3 历史 | 4 设置 | 5 检查更新 | 6 退出
#     Callers must pass the index of the item they mean and should also screenshot the open
#     menu so the actual label is visible in the evidence.
#
# Actions:
#   -Index N      open the menu and click text band N (default 2 = the pins toggle)
#   -List         open the menu and only print the bands (no click)
#   -Shot <path>  save a PNG of the whole primary screen with the menu open
#
# Exit codes: 0 ok / 1 taskbar or tray icon not found / 2 menu not found / 3 band missing
param(
    [int]$Index = 2,
    [switch]$List,
    [string]$Shot = ''
)
$ErrorActionPreference = 'Stop'

try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -TypeDefinition @'
using System;
using System.Text;
using System.Runtime.InteropServices;

public static class ZeepCutTrayMenu
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    public delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool SetProcessDpiAwarenessContext(IntPtr value);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);

    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetClassName(IntPtr hWnd, StringBuilder text, int maxCount);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

    public const uint LEFTDOWN = 0x0002;
    public const uint LEFTUP = 0x0004;
    public const uint RIGHTDOWN = 0x0008;
    public const uint RIGHTUP = 0x0010;

    public static void Click(int x, int y, bool right)
    {
        SetCursorPos(x, y);
        System.Threading.Thread.Sleep(220);
        if (right)
        {
            mouse_event(RIGHTDOWN, 0, 0, 0, UIntPtr.Zero);
            mouse_event(RIGHTUP, 0, 0, 0, UIntPtr.Zero);
        }
        else
        {
            mouse_event(LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
            mouse_event(LEFTUP, 0, 0, 0, UIntPtr.Zero);
        }
    }

    public static IntPtr FindPopupMenu()
    {
        IntPtr found = IntPtr.Zero;
        EnumWindows(delegate (IntPtr h, IntPtr l)
        {
            if (!IsWindowVisible(h))
            {
                return true;
            }

            var cls = new StringBuilder(64);
            GetClassName(h, cls, 64);
            if (cls.ToString() == "#32768")
            {
                found = h;
            }

            return true;
        }, IntPtr.Zero);
        return found;
    }
}
'@

[ZeepCutTrayMenu]::SetProcessDpiAwarenessContext([IntPtr](-4)) | Out-Null

function Find-ByName {
    param($Root, [string]$Name)
    $condition = New-Object System.Windows.Automation.PropertyCondition -ArgumentList @(
        [System.Windows.Automation.AutomationElement]::NameProperty, $Name)
    return $Root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
}

$desktop = [System.Windows.Automation.AutomationElement]::RootElement
$taskbarCondition = New-Object System.Windows.Automation.PropertyCondition -ArgumentList @(
    [System.Windows.Automation.AutomationElement]::ClassNameProperty, 'Shell_TrayWnd')
$taskbar = $desktop.FindFirst([System.Windows.Automation.TreeScope]::Children, $taskbarCondition)
if (-not $taskbar) { Write-Host 'TASKBAR NOT FOUND' -ForegroundColor Red; exit 1 }

# The chevron's accessible name changes with the flyout state ("Show Hidden Icons" /
# "Show Hidden Icons Hide"), so match it by prefix inside the automation-id bucket.
$chevron = $null
$chevronCondition = New-Object System.Windows.Automation.PropertyCondition -ArgumentList @(
    [System.Windows.Automation.AutomationElement]::AutomationIdProperty, 'SystemTrayIcon')
$trayButtons = $taskbar.FindAll([System.Windows.Automation.TreeScope]::Descendants, $chevronCondition)
foreach ($candidate in $trayButtons) {
    if ($candidate.Current.Name -like 'Show Hidden Icons*') { $chevron = $candidate; break }
}

if (-not $chevron) { Write-Host 'OVERFLOW CHEVRON NOT FOUND' -ForegroundColor Red; exit 1 }
$cr = $chevron.Current.BoundingRectangle
Write-Host ("OVERFLOW CHEVRON {0},{1} {2}x{3}" -f $cr.X, $cr.Y, $cr.Width, $cr.Height)
function Find-TrayIcon {
    $nameCondition = New-Object System.Windows.Automation.PropertyCondition -ArgumentList @(
        [System.Windows.Automation.AutomationElement]::NameProperty, 'Zeep Cut')
    $found = $desktop.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $nameCondition)
    if ($found) { return $found }

    $notifyCondition = New-Object System.Windows.Automation.PropertyCondition -ArgumentList @(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty, 'NotifyItemIcon')
    $notifyIcons = $desktop.FindAll([System.Windows.Automation.TreeScope]::Descendants, $notifyCondition)
    foreach ($candidate in $notifyIcons) {
        if ($candidate.Current.Name -like '*Zeep Cut*') { return $candidate }
    }

    return $null
}

# The chevron TOGGLES the overflow flyout, so a flyout left open by an earlier run would be
# closed by this click. Try up to three times and re-click when the icon does not show up.
$icon = $null
for ($attempt = 1; $attempt -le 3; $attempt++) {
    [ZeepCutTrayMenu]::Click([int]($cr.X + $cr.Width / 2), [int]($cr.Y + $cr.Height / 2), $false)
    Start-Sleep -Milliseconds 900
    $icon = Find-TrayIcon
    if ($icon) { break }
    Write-Host ("TRAY ICON NOT VISIBLE (attempt {0}/3), toggling the overflow flyout again" -f $attempt)
    Start-Sleep -Milliseconds 400
}

if (-not $icon) {
    Write-Host 'ZEEPCUT TRAY ICON NOT FOUND IN FLYOUT' -ForegroundColor Red
    exit 1
}

$ir = $icon.Current.BoundingRectangle
Write-Host ("ZEEPCUT TRAY ICON {0},{1} {2}x{3}" -f $ir.X, $ir.Y, $ir.Width, $ir.Height)
[ZeepCutTrayMenu]::Click([int]($ir.X + $ir.Width / 2), [int]($ir.Y + $ir.Height / 2), $true)
Start-Sleep -Milliseconds 900

$menu = [ZeepCutTrayMenu]::FindPopupMenu()
if ($menu -eq [IntPtr]::Zero) { Write-Host 'POPUP MENU NOT FOUND' -ForegroundColor Red; exit 2 }

$menuRect = New-Object ZeepCutTrayMenu+RECT
[ZeepCutTrayMenu]::GetWindowRect($menu, [ref]$menuRect) | Out-Null
Write-Host ("POPUP MENU hwnd=0x{0:X} {1},{2} {3}x{4}" -f $menu.ToInt64(), $menuRect.Left, $menuRect.Top, ($menuRect.Right - $menuRect.Left), ($menuRect.Bottom - $menuRect.Top))

if ($Shot) {
    $bmp = New-Object System.Drawing.Bitmap(2560, 1440)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen(0, 0, 0, 0, $bmp.Size)
    $g.Dispose()
    $bmp.Save($Shot, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Host ("SAVED MENU SHOT {0}" -f $Shot)
}

# Text rows: a menu item's label is dark pixels on the light menu background. Separators have
# no pixels at all, so they simply do not produce a band.
$bmp = New-Object System.Drawing.Bitmap(2560, 1440)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen(0, 0, 0, 0, $bmp.Size)
$g.Dispose()

$rows = New-Object System.Collections.ArrayList
for ($y = $menuRect.Top; $y -lt $menuRect.Bottom; $y++) {
    $dark = 0
    $right = [Math]::Min($menuRect.Left + 170, $menuRect.Right)
    for ($x = $menuRect.Left + 10; $x -lt $right; $x++) {
        $c = $bmp.GetPixel($x, $y)
        if (($c.R + $c.G + $c.B) -lt 480) { $dark++ }
    }
    [void]$rows.Add([pscustomobject]@{ Y = $y; Dark = $dark })
}
$bmp.Dispose()

$bands = New-Object System.Collections.ArrayList
$start = -1
foreach ($row in $rows) {
    if ($row.Dark -gt 2) {
        if ($start -lt 0) { $start = $row.Y }
    }
    elseif ($start -ge 0) {
        if (($row.Y - $start) -ge 6) {
            [void]$bands.Add([pscustomobject]@{ Top = $start; Bottom = $row.Y - 1 })
        }
        $start = -1
    }
}
if ($start -ge 0) { [void]$bands.Add([pscustomobject]@{ Top = $start; Bottom = $menuRect.Bottom - 1 }) }

Write-Host ("MENU TEXT BANDS = {0} (order: 0 截图 | 1 从剪贴板贴图 | 2 显示或隐藏全部贴图 | 3 历史 | 4 设置 | 5 检查更新 | 6 退出)" -f $bands.Count)
for ($i = 0; $i -lt $bands.Count; $i++) {
    Write-Host ("  BAND {0} y={1}..{2}" -f $i, $bands[$i].Top, $bands[$i].Bottom)
}

if ($List) {
    [System.Windows.Forms.SendKeys]::SendWait('{ESC}')
    Write-Host 'LIST ONLY (menu closed with ESC)'
    exit 0
}

if ($Index -lt 0 -or $Index -ge $bands.Count) {
    Write-Host ("BAND INDEX {0} OUT OF RANGE (0..{1})" -f $Index, ($bands.Count - 1)) -ForegroundColor Red
    [System.Windows.Forms.SendKeys]::SendWait('{ESC}')
    exit 3
}

$band = $bands[$Index]
$clickX = $menuRect.Left + 60
$clickY = [int](($band.Top + $band.Bottom) / 2)
Write-Host ("CLICK MENU BAND {0} at {1},{2}" -f $Index, $clickX, $clickY)
[ZeepCutTrayMenu]::Click($clickX, $clickY, $false)
Start-Sleep -Milliseconds 900
exit 0
