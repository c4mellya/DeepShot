$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$source = Join-Path $root 'artifacts\tools\innosetup-6.7.3.exe'
$destination = Join-Path $env:TEMP 'ZeepCut-InnoSetup6'
$setupLog = Join-Path $env:TEMP 'ZeepCut-InnoSetup6.log'
$command = '"' + $source + '" /SP- /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CURRENTUSER /NOICONS /DIR="' + $destination + '" /LOG="' + $setupLog + '"'
$result = Invoke-WmiMethod -Class Win32_Process -Name Create -ArgumentList $command
if ($result.ReturnValue -ne 0) { throw "Compiler setup could not start: $($result.ReturnValue)" }
Wait-Process -Id $result.ProcessId -Timeout 120 -ErrorAction SilentlyContinue
$compiler = Join-Path $destination 'ISCC.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw 'Compiler installation did not produce ISCC.exe' }
$target = Join-Path $root 'artifacts\tools\InnoSetup'
New-Item -ItemType Directory -Force -Path $target | Out-Null
Copy-Item -Path (Join-Path $destination '*') -Destination $target -Recurse -Force
Write-Host "Compiler ready: $target"
