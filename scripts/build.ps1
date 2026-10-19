# Build the application only. Does not run tests or launch DeepShot.
param(
    [ValidateSet('Debug','Release')][string]$Configuration = 'Release',
    [switch]$NoRestore
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$buildArgs = @('build', (Join-Path $root 'DeepShot.slnx'), '-c', $Configuration, '-v', 'minimal', '-nologo')
if ($NoRestore) { $buildArgs += '--no-restore' }
& dotnet @buildArgs
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Write-Host 'BUILD OK' -ForegroundColor Green
