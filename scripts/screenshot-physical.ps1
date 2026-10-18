# Capture physical pixels of the real desktop (DPI-aware).
#
# screenshot.ps1 is a DPI-unaware WinForms script, so on the 125% primary screen it
# captures a virtualized 2048x1152 image. This script declares PerMonitorV2 first and
# then captures the true physical pixels (2560x1440 primary / 1080x1920 secondary),
# which is what the app and its overlay are expected to match.
#
# Examples:
#   ... screenshot-physical.ps1 -List
#   ... screenshot-physical.ps1 -List
#   ... screenshot-physical.ps1 -Primary -Out C:\temp\primary.png
#   ... screenshot-physical.ps1 -DeviceName '\\.\DISPLAY2' -Out C:\temp\secondary.png
#   ... screenshot-physical.ps1 -MonitorIndex 1 -Out C:\temp\any.png
param(
    [string]$Out = '',
    [int]$MonitorIndex = -1,
    [string]$DeviceName = '',
    [switch]$Primary,
    [switch]$List
)
$ErrorActionPreference = 'Stop'

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

public static class ZeepCutDpi
{
    [DllImport("user32.dll")]
    public static extern bool SetProcessDpiAwarenessContext(IntPtr value);
}
'@

# -4 = DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2
[ZeepCutDpi]::SetProcessDpiAwarenessContext([IntPtr](-4)) | Out-Null

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

$screens = [System.Windows.Forms.Screen]::AllScreens
if ($screens.Count -eq 0) { Write-Host 'no screens' -ForegroundColor Red; exit 1 }

for ($i = 0; $i -lt $screens.Count; $i++) {
    $b = $screens[$i].Bounds
    Write-Host ("screen {0}: {1} origin=({2},{3}) {4}x{5} primary={6}" -f $i, $screens[$i].DeviceName, $b.Left, $b.Top, $b.Width, $b.Height, $screens[$i].Primary)
}
if ($List) { exit 0 }

# 屏幕枚举顺序不保证与 DISPLAY 编号一致；优先按主屏/设备名定位，避免截错屏。
if ($Primary) {
    $MonitorIndex = -1
    for ($i = 0; $i -lt $screens.Count; $i++) {
        if ($screens[$i].Primary) { $MonitorIndex = $i; break }
    }
    if ($MonitorIndex -lt 0) { Write-Host 'primary screen not found' -ForegroundColor Red; exit 1 }
}
elseif (-not [string]::IsNullOrWhiteSpace($DeviceName)) {
    $MonitorIndex = -1
    for ($i = 0; $i -lt $screens.Count; $i++) {
        if ($screens[$i].DeviceName -eq $DeviceName) { $MonitorIndex = $i; break }
    }
    if ($MonitorIndex -lt 0) { Write-Host ("screen not found: {0}" -f $DeviceName) -ForegroundColor Red; exit 1 }
}

if ([string]::IsNullOrWhiteSpace($Out)) {
    $Out = Join-Path $env:TEMP 'zeepcut-physical.png'
}

if ($MonitorIndex -ge 0) {
    if ($MonitorIndex -ge $screens.Count) { Write-Host "screen index out of range ($($screens.Count))" -ForegroundColor Red; exit 1 }
    $r = $screens[$MonitorIndex].Bounds
} else {
    $x1 = ($screens | ForEach-Object { $_.Bounds.Left } | Measure-Object -Minimum).Minimum
    $y1 = ($screens | ForEach-Object { $_.Bounds.Top } | Measure-Object -Minimum).Minimum
    $x2 = ($screens | ForEach-Object { $_.Bounds.Right } | Measure-Object -Maximum).Maximum
    $y2 = ($screens | ForEach-Object { $_.Bounds.Bottom } | Measure-Object -Maximum).Maximum
    $r = New-Object System.Drawing.Rectangle($x1, $y1, ($x2 - $x1), ($y2 - $y1))
}

$bmp = New-Object System.Drawing.Bitmap($r.Width, $r.Height)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($r.Left, $r.Top, 0, 0, $bmp.Size)
$g.Dispose()
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
Write-Host ("SAVED {0} ({1}x{2}) origin=({3},{4})" -f $Out, $r.Width, $r.Height, $r.Left, $r.Top)
