param(
    [switch]$SkipDeploy,
    [string]$Configuration = 'Release'
)

# Package helper for DSPQualitySystem.
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\package.ps1
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\package.ps1 -SkipDeploy
#
# Steps: build + deploy (unless -SkipDeploy) -> zip bin\<Configuration>\DSPQualitySystem.dll
# and README.md into dist\DSPQualitySystem-<version>.zip.
# Success markers: PACKAGE OK / PACKAGE FAILED.

$ErrorActionPreference = 'Stop'

$root = Split-Path $PSScriptRoot -Parent
$pluginSrc = Join-Path $root 'Plugin.cs'
$dll = Join-Path $root ('bin\' + $Configuration + '\DSPQualitySystem.dll')
$readme = Join-Path $root 'README.md'
$dist = Join-Path $root 'dist'

if (-not (Test-Path $pluginSrc)) {
    Write-Host ('PACKAGE FAILED: missing ' + $pluginSrc)
    exit 1
}

$versionMatch = [regex]::Match([IO.File]::ReadAllText($pluginSrc), 'VERSION\s*=\s*"([0-9A-Za-z\.\-]+)"')
if (-not $versionMatch.Success) {
    Write-Host 'PACKAGE FAILED: cannot read VERSION from Plugin.cs'
    exit 1
}
$version = $versionMatch.Groups[1].Value

if (-not $SkipDeploy) {
    $build = Join-Path $root 'build.ps1'
    $log = & powershell -NoProfile -ExecutionPolicy Bypass -File $build -Deploy -Configuration $Configuration 2>&1 |
        ForEach-Object { Write-Host $_; "$_" }
    if (($log -join "`n") -notmatch 'DEPLOY OK') {
        Write-Host 'PACKAGE FAILED: build/deploy failed (see output above; is the game running?)'
        exit 1
    }
}

if (-not (Test-Path $dll)) {
    Write-Host ('PACKAGE FAILED: missing ' + $dll)
    exit 1
}
if (-not (Test-Path $readme)) {
    Write-Host ('PACKAGE FAILED: missing ' + $readme)
    exit 1
}

Add-Type -AssemblyName System.IO.Compression.FileSystem

if (-not (Test-Path $dist)) {
    New-Item -ItemType Directory -Force -Path $dist | Out-Null
}
$zip = Join-Path $dist ('DSPQualitySystem-' + $version + '.zip')
if (Test-Path $zip) { Remove-Item $zip -Force }

$staging = Join-Path ([IO.Path]::GetTempPath()) ('DSPQualitySystem-pkg-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $staging | Out-Null
try {
    Copy-Item -Path $dll -Destination (Join-Path $staging 'DSPQualitySystem.dll') -Force
    Copy-Item -Path $readme -Destination (Join-Path $staging 'README.md') -Force
    [IO.Compression.ZipFile]::CreateFromDirectory($staging, $zip)

    $zipHash = (Get-FileHash -Path $zip -Algorithm SHA256).Hash
    $dllHash = (Get-FileHash -Path $dll -Algorithm SHA256).Hash
    $zipSize = (Get-Item $zip).Length

    Write-Host ('PACKAGE OK -> ' + $zip)
    Write-Host ('PACKAGE VERSION ' + $version)
    Write-Host ('PACKAGE SIZE ' + $zipSize)
    Write-Host ('PACKAGE SHA256 ' + $zipHash)
    Write-Host ('PACKAGE DLL SHA256 ' + $dllHash)
}
catch {
    Write-Host ('PACKAGE FAILED: ' + $_.Exception.Message)
    exit 1
}
finally {
    if (Test-Path $staging) { Remove-Item $staging -Recurse -Force -ErrorAction SilentlyContinue }
}

exit 0
