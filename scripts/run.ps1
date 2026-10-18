# Launch the built app, optionally wait and report the process.
#
# 注意：在本机 Windows PowerShell 5.1 环境下 Start-Process 会因环境里同时存在
# NO_PROXY 与 no_proxy（大小写重复键）抛出 "Item has already been added"；
# 另外 GUI 进程会继承 WSL 侧管道导致调用方一直等待。因此改用 WMI 创建进程，
# 它立即返回、不继承管道，也绕开 Start-Process 的环境字典问题。
param(
    [ValidateSet('Debug','Release')][string]$Configuration = 'Debug',
    [int]$WaitSeconds = 5
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $root "src\ZeepCut\bin\$Configuration\net10.0-windows\ZeepCut.exe"
if (-not (Test-Path $exe)) { Write-Host "missing $exe - run build.ps1 first" -ForegroundColor Red; exit 1 }

$result = Invoke-CimMethod -ClassName Win32_Process -MethodName Create -Arguments @{ CommandLine = ('"' + $exe + '"') }
if ($result.ReturnValue -ne 0) {
    Write-Host ("FAILED to start, Win32_Process.Create returned {0}" -f $result.ReturnValue) -ForegroundColor Red
    exit 1
}

Write-Host ("STARTED pid {0}" -f $result.ProcessId)
Start-Sleep -Seconds $WaitSeconds
$p = Get-Process -Id $result.ProcessId -ErrorAction SilentlyContinue
if (-not $p) {
    Write-Host ("EXITED early, pid {0}" -f $result.ProcessId) -ForegroundColor Red
    exit 2
}
Write-Host ("RUNNING pid {0} title '{1}'" -f $p.Id, $p.MainWindowTitle)
