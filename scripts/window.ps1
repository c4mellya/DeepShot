# Move/resize a top-level window by physical screen coordinates (DPI-aware).
# Used to place a test window on the negative-coordinate secondary screen.
# Example: -ProcessName notepad -X -950 -Y 300 -Width 500 -Height 400
param(
    [Parameter(Mandatory=$true)][string]$ProcessName,
    [Parameter(Mandatory=$true)][int]$X,
    [Parameter(Mandatory=$true)][int]$Y,
    [int]$Width = 600,
    [int]$Height = 400
)
$ErrorActionPreference = 'Stop'

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

public static class ZeepCutWindow
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    public static extern bool SetProcessDpiAwarenessContext(IntPtr value);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
}
'@

# -4 = DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2
[ZeepCutWindow]::SetProcessDpiAwarenessContext([IntPtr](-4)) | Out-Null

$p = Get-Process -Name $ProcessName -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
if (-not $p) { Write-Host "window not found: $ProcessName" -ForegroundColor Red; exit 1 }

$hwnd = $p.MainWindowHandle
[ZeepCutWindow]::ShowWindow($hwnd, 9) | Out-Null  # SW_RESTORE
Start-Sleep -Milliseconds 150

# SWP_NOZORDER | SWP_NOACTIVATE
if (-not [ZeepCutWindow]::SetWindowPos($hwnd, [IntPtr]::Zero, $X, $Y, $Width, $Height, 0x0004 -bor 0x0010)) {
    Write-Host ("SetWindowPos failed, error {0}" -f [Runtime.InteropServices.Marshal]::GetLastWin32Error()) -ForegroundColor Red
    exit 1
}

$rect = New-Object 'ZeepCutWindow+RECT'
if ([ZeepCutWindow]::GetWindowRect($hwnd, [ref]$rect)) {
    Write-Host ("WINDOW {0} hwnd=0x{1:X} rect=({2},{3}) {4}x{5}" -f $p.ProcessName, $hwnd.ToInt64(), $rect.Left, $rect.Top, ($rect.Right - $rect.Left), ($rect.Bottom - $rect.Top))
}
