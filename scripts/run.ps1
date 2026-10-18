# Launch the built app, optionally wait and report the process.
param(
    [ValidateSet('Debug','Release')][string]$Configuration = 'Debug',
    [int]$WaitSeconds = 5
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $root "src\ZeepCut\bin\$Configuration\net10.0-windows\ZeepCut.exe"
if (-not (Test-Path $exe)) { Write-Host "missing $exe - run build.ps1 first" -ForegroundColor Red; exit 1 }
$p = Start-Process $exe -PassThru
Start-Sleep -Seconds $WaitSeconds
if ($p.HasExited) {
    Write-Host ("EXITED early, exit code {0}" -f $p.ExitCode) -ForegroundColor Red
    exit 2
}
Write-Host ("RUNNING pid {0} title '{1}'" -f $p.Id, $p.MainWindowTitle)
