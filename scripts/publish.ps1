param([string]$InnoCompiler = '', [switch]$NoRestore)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src\DeepShot\DeepShot.csproj'
[xml]$projectXml = Get-Content -LiteralPath $project -Raw
$version = [string]$projectXml.Project.PropertyGroup.Version
$output = Join-Path $root 'artifacts\publish\DeepShot'
$release = Join-Path $root 'artifacts\release'
New-Item -ItemType Directory -Force -Path $release | Out-Null

# 清掉上一次发布目录，避免旧版本残留文件被打进安装包。
if (Test-Path -LiteralPath $output) {
    Remove-Item -LiteralPath $output -Recurse -Force
}

# Publish only: intentionally does not invoke the test project or launch the application.
$publishArgs = @(
    'publish', $project, '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true',
    '-p:PublishSingleFile=false', '-p:PublishTrimmed=false', '-o', $output, '--nologo'
)
if ($NoRestore) { $publishArgs += '--no-restore' }
& dotnet @publishArgs
if ($LASTEXITCODE -ne 0) { throw "Publish failed ($LASTEXITCODE)" }
foreach ($name in @('README.md', 'README.en.md', 'RELEASE_NOTES.md')) {
    Copy-Item -LiteralPath (Join-Path $root $name) -Destination $output -Force
}
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = Join-Path $release "DeepShot-$version-win-x64.zip"
if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
[System.IO.Compression.ZipFile]::CreateFromDirectory($output, $zip, [System.IO.Compression.CompressionLevel]::Optimal, $true)

if (-not $InnoCompiler) {
    $candidates = @(
        "${env:ProgramFiles(x86)}\Inno Setup 7\ISCC.exe",
        "${env:ProgramFiles}\Inno Setup 7\ISCC.exe",
        "$env:LOCALAPPDATA\Programs\Inno Setup 7\ISCC.exe",
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles}\Inno Setup 6\ISCC.exe",
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
    )
    $InnoCompiler = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}
if ($InnoCompiler) {
    & $InnoCompiler "/DAppVersion=$version" "/DSourceDir=$output" (Join-Path $root 'installer\DeepShot.iss')
    if ($LASTEXITCODE -ne 0) { throw "Installer compilation failed ($LASTEXITCODE)" }
} else {
    Write-Warning 'Inno Setup was not found. ZIP is available; pass -InnoCompiler to build the installer.'
}
Get-ChildItem -LiteralPath $release -File | Where-Object { $_.Extension -in @('.zip', '.exe') } | ForEach-Object {
    Write-Host "RELEASE: $($_.FullName) ($($_.Length) bytes)"
}
