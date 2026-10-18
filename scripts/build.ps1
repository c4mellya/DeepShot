# Build + test Zeep Cut. Run from WSL:
#   powershell.exe -NoProfile -ExecutionPolicy Bypass -File '\\wsl.localhost\Ubuntu\home\camellya\workspace\Zeep-Cut\scripts\build.ps1'
param(
    [ValidateSet('Debug','Release')][string]$Configuration = 'Debug',
    [switch]$SkipTests,
    [switch]$NoRestore
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

Write-Host "== restore/build ($Configuration) ==" -ForegroundColor Cyan
$buildArgs = @('build', (Join-Path $root 'ZeepCut.slnx'), '-c', $Configuration, '-v', 'minimal', '-nologo')
if ($NoRestore) { $buildArgs += '--no-restore' }
& dotnet @buildArgs
if ($LASTEXITCODE -ne 0) { Write-Host 'BUILD FAILED' -ForegroundColor Red; exit $LASTEXITCODE }

if (-not $SkipTests) {
    Write-Host '== test ==' -ForegroundColor Cyan
    & dotnet test (Join-Path $root 'tests\ZeepCut.Tests\ZeepCut.Tests.csproj') -c $Configuration --nologo -v minimal
    if ($LASTEXITCODE -ne 0) { Write-Host 'TESTS FAILED' -ForegroundColor Red; exit $LASTEXITCODE }
}
Write-Host 'BUILD+TEST OK' -ForegroundColor Green
