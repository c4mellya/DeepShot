param([string]$InnoCompiler = '')
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src\ZeepCut\ZeepCut.csproj'
[xml]$projectXml = Get-Content -LiteralPath $project -Raw
$version = [string]$projectXml.Project.PropertyGroup.Version
$output = Join-Path $root 'artifacts\publish\win-x64'
$release = Join-Path $root 'artifacts\release'
New-Item -ItemType Directory -Force -Path $release | Out-Null

# Publish only: intentionally does not invoke the test project or launch the application.
& dotnet publish $project -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:PublishTrimmed=false -o $output --nologo
if ($LASTEXITCODE -ne 0) { throw "Publish failed ($LASTEXITCODE)" }
foreach ($name in @('README.md', 'README.en.md', 'RELEASE_NOTES.md', 'ACCEPTANCE.md')) {
    Copy-Item -LiteralPath (Join-Path $root $name) -Destination $output -Force
}
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = Join-Path $release "ZeepCut-$version-win-x64.zip"
if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
[System.IO.Compression.ZipFile]::CreateFromDirectory($output, $zip, [System.IO.Compression.CompressionLevel]::Optimal, $false)

if (-not $InnoCompiler) {
    $candidates = @(
        (Join-Path $root 'artifacts\tools\InnoSetup\ISCC.exe'),
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
    )
    $InnoCompiler = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}
if ($InnoCompiler) {
    & $InnoCompiler "/DAppVersion=$version" "/DSourceDir=$output" (Join-Path $root 'installer\ZeepCut.iss')
    if ($LASTEXITCODE -ne 0) { throw "Installer compilation failed ($LASTEXITCODE)" }
} else {
    Write-Warning 'Inno Setup was not found. ZIP is available; pass -InnoCompiler to build the installer.'
}
Get-ChildItem -LiteralPath $release -File | Where-Object { $_.Extension -in @('.zip', '.exe') } | ForEach-Object {
    $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $($_.Name)" | Set-Content -LiteralPath ($_.FullName + '.sha256') -Encoding ASCII
    Write-Host "RELEASE: $($_.FullName) ($($_.Length) bytes)"
}
