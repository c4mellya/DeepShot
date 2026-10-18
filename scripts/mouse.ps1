# Move the mouse, click, or drag at absolute physical screen coordinates.
#
# The script declares PerMonitorV2 before calling SetCursorPos/GetCursorPos, so the
# coordinates are true physical pixels on mixed-DPI setups (primary 125% + secondary 100%).
#
# Examples:
#   -X 1900 -Y 1120 -Button right              # right click
#   -X 300 -Y 300 -ToX 940 -ToY 780            # real left-button drag 300,300 -> 940,780
#   -X 500 -Y 500 -Button move                 # move only
param(
    [Parameter(Mandatory=$true)][int]$X,
    [Parameter(Mandatory=$true)][int]$Y,
    [ValidateSet('left','right','move')][string]$Button = 'left',
    [switch]$DoubleClick,
    [int]$ToX = [int]::MinValue,
    [int]$ToY = [int]::MinValue,
    [int]$Steps = 12,
    [int]$StepDelayMs = 25,
    [int]$PressDelayMs = 80
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

function Get-CursorText {
    $p = New-Object 'ZeepCutMouse+POINT'
    if ([ZeepCutMouse]::GetCursorPos([ref]$p)) {
        return ("{0},{1}" -f $p.X, $p.Y)
    }
    return 'unknown'
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

[ZeepCutMouse]::SetCursorPos($X, $Y) | Out-Null
Start-Sleep -Milliseconds 150
Write-Host ("CURSOR {0}" -f (Get-CursorText))

$isDrag = ($ToX -ne [int]::MinValue) -or ($ToY -ne [int]::MinValue)

if ($isDrag) {
    if ($ToX -eq [int]::MinValue -or $ToY -eq [int]::MinValue) {
        Write-Host 'DRAG requires both -ToX and -ToY' -ForegroundColor Red
        exit 1
    }
    if ($Button -eq 'right') {
        Write-Host 'DRAG supports the left button only' -ForegroundColor Red
        exit 1
    }
    if ($Steps -lt 1) { $Steps = 1 }

    [ZeepCutMouse]::mouse_event([ZeepCutMouse]::LEFTDOWN, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds $PressDelayMs

    for ($i = 1; $i -le $Steps; $i++) {
        $cx = [int][Math]::Round($X + (($ToX - $X) * $i / $Steps))
        $cy = [int][Math]::Round($Y + (($ToY - $Y) * $i / $Steps))
        [ZeepCutMouse]::SetCursorPos($cx, $cy) | Out-Null
        Start-Sleep -Milliseconds $StepDelayMs
    }

    [ZeepCutMouse]::SetCursorPos($ToX, $ToY) | Out-Null
    Start-Sleep -Milliseconds 120
    [ZeepCutMouse]::mouse_event([ZeepCutMouse]::LEFTUP, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 120

    Write-Host ("DRAG {0},{1} -> {2},{3} steps={4} cursor={5}" -f $X, $Y, $ToX, $ToY, $Steps, (Get-CursorText))
    exit 0
}

if ($Button -eq 'move') {
    Write-Host ("MOVED {0},{1}" -f $X, $Y)
    exit 0
}

Invoke-Click $Button
if ($DoubleClick) {
    Start-Sleep -Milliseconds 80
    Invoke-Click $Button
}
Write-Host ("CLICK {0} at {1},{2}{3}" -f $Button, $X, $Y, $(if ($DoubleClick) { ' (double)' } else { '' }))
