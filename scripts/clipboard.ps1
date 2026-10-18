# Read/save the clipboard image from an INDEPENDENT process (verification only).
#
# The app is a WPF STA process; verification reads the clipboard from this separate
# process so it never shares clipboard state with the app. WinForms is allowed here
# because this is a verification script (the app itself is WPF + Win32 only).
#
# Examples:
#   ... clipboard.ps1 -Save \\wsl.localhost\Ubuntu\...\artifacts\t11\clipboard.png
#   ... clipboard.ps1 -Clear
#   ... clipboard.ps1 -HoldSeconds 8
param(
    [string]$Save = '',
    [int]$HoldSeconds = 0,
    [switch]$Clear
)
$ErrorActionPreference = 'Stop'

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

public static class ZeepCutClipboard
{
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool CloseClipboard();
}
'@

# 按住剪贴板不放，用于复现"剪贴板被占用"的失败路径。
if ($HoldSeconds -gt 0) {
    if (-not [ZeepCutClipboard]::OpenClipboard([IntPtr]::Zero)) {
        $err = [System.Runtime.InteropServices.Marshal]::GetLastWin32Error()
        Write-Host ("CLIPBOARD OPEN FAILED win32={0}" -f $err) -ForegroundColor Red
        exit 2
    }
    Write-Host ("CLIPBOARD HELD {0}s pid={1}" -f $HoldSeconds, $PID)
    Start-Sleep -Seconds $HoldSeconds
    [ZeepCutClipboard]::CloseClipboard() | Out-Null
    Write-Host 'CLIPBOARD RELEASED'
    exit 0
}

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

if ($Clear) {
    [System.Windows.Forms.Clipboard]::Clear()
    Write-Host 'CLIPBOARD CLEARED'
    exit 0
}

$image = [System.Windows.Forms.Clipboard]::GetImage()
if (-not $image) {
    Write-Host 'CLIPBOARD HAS NO IMAGE' -ForegroundColor Yellow
    exit 1
}

Write-Host ("CLIPBOARD IMAGE {0}x{1} format={2}" -f $image.Width, $image.Height, $image.RawFormat.Guid)
if (-not [string]::IsNullOrWhiteSpace($Save)) {
    $dir = Split-Path -Parent $Save
    if ($dir -and -not (Test-Path $dir)) {
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
    }
    $image.Save($Save, [System.Drawing.Imaging.ImageFormat]::Png)
    Write-Host ("SAVED {0}" -f $Save)
}

$image.Dispose()
