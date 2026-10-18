# Send keystrokes to the foreground window (or activate a process first).
# Example: -Keys '^%a' sends Ctrl+Alt+A. -ProcessName ZeepCut activates it first.
param(
    [Parameter(Mandatory=$true)][string]$Keys,
    [string]$ProcessName = ''
)
Add-Type -AssemblyName System.Windows.Forms
if ($ProcessName) {
    $p = Get-Process -Name $ProcessName -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $p) { Write-Host "process not found: $ProcessName" -ForegroundColor Red; exit 1 }
    Start-Sleep -Milliseconds 400
}
[System.Windows.Forms.SendKeys]::SendWait($Keys)
Write-Host "SENT $Keys"
