# Move the mouse and click at absolute physical screen coordinates.
#
# The script declares PerMonitorV2 before calling SetCursorPos/GetCursorPos, so the
# coordinates are true physical pixels on mixed-DPI setups (primary 125% + secondary 100%).
# Example: -X 1900 -Y 1120 -Button right
param(
    [Parameter(Mandatory=$true)][int]$X,
    [Parameter(Mandatory=$true)][int]$Y,
    [ValidateSet('left','right','move')][string]$Button = 'left',
    [switch]$DoubleClick
)
$ErrorActionPreference = 'Stop'

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

public static class ZeepCutMouse
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

    public const uint LEFTDOWN = 0x0002;
    public const uint LEFTUP = 0x0004;
    public const uint RIGHTDOWN = 0x0008;
    public const uint RIGHTUP = 0x0010;
}
'@

# -4 = DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2
[ZeepCutMouse]::SetProcessDpiAwarenessContext([IntPtr](-4)) | Out-Null

[ZeepCutMouse]::SetCursorPos($X, $Y) | Out-Null
Start-Sleep -Milliseconds 150

$point = New-Object 'ZeepCutMouse+POINT'
if ([ZeepCutMouse]::GetCursorPos([ref]$point)) {
    Write-Host ("CURSOR {0},{1}" -f $point.X, $point.Y)
}

if ($Button -eq 'move') {
    Write-Host ("MOVED {0},{1}" -f $X, $Y)
    exit 0
}

function Invoke-Click([string]$which) {
    if ($which -eq 'right') {
        [ZeepCutMouse]::mouse_event([ZeepCutMouse]::RIGHTDOWN, 0, 0, 0, [UIntPtr]::Zero)
        Start-Sleep -Milliseconds 60
        [ZeepCutMouse]::mouse_event([ZeepCutMouse]::RIGHTUP, 0, 0, 0, [UIntPtr]::Zero)
    } else {
        [ZeepCutMouse]::mouse_event([ZeepCutMouse]::LEFTDOWN, 0, 0, 0, [UIntPtr]::Zero)
        Start-Sleep -Milliseconds 60
        [ZeepCutMouse]::mouse_event([ZeepCutMouse]::LEFTUP, 0, 0, 0, [UIntPtr]::Zero)
    }
}

Invoke-Click $Button
if ($DoubleClick) {
    Start-Sleep -Milliseconds 80
    Invoke-Click $Button
}
Write-Host ("CLICK {0} at {1},{2}{3}" -f $Button, $X, $Y, $(if ($DoubleClick) { ' (double)' } else { '' }))
