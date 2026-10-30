param(
    [string]$Configuration = 'Release',
    [switch]$Deploy
)

# Build helper for DSPQualitySystem.
#   powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1
#   powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1 -Deploy
#
# Optional environment:
#   DSP_TOOLS_DIR  folder with dspenv.ps1 (default: sibling DSP-MPD-runtime-debug\tools)
#   DSP_DOTNET     dotnet driver to use (default: dspenv.ps1 sets it; otherwise "dotnet" on PATH)
#   DSP_GAME_DIR   game install folder (default: known Steam paths; if absent, compile uses tools\unity-refs)
#
# Success is reported with explicit markers: BUILD OK / BUILD FAILED / DEPLOY OK / DEPLOY FAILED.

$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot

function Find-GameDir {
    if ($env:DSP_GAME_DIR) { return $env:DSP_GAME_DIR }
    $candidates = @(
        'C:\Games\Steam\steamapps\common\Dyson Sphere Program',
        'C:\Program Files (x86)\Steam\steamapps\common\Dyson Sphere Program',
        'D:\SteamLibrary\steamapps\common\Dyson Sphere Program',
        'E:\SteamLibrary\steamapps\common\Dyson Sphere Program'
    )
    foreach ($c in $candidates) {
        if (Test-Path (Join-Path $c 'DSPGAME_Data\Managed\Assembly-CSharp.dll')) { return $c }
    }
    return $null
}

# Prefer the workspace toolchain (portable SDK + redirected NuGet cache), else plain dotnet.
$tools = if ($env:DSP_TOOLS_DIR) { $env:DSP_TOOLS_DIR } else { Join-Path (Split-Path $root -Parent) 'DSP-MPD-runtime-debug\tools' }
$dspenv = Join-Path $tools 'dspenv.ps1'
if (Test-Path $dspenv) {
    . $dspenv
} else {
    Write-Host ('[env] no dspenv.ps1 at ' + $dspenv + ' - using dotnet from PATH')
    $DspDotnet = if ($env:DSP_DOTNET) { $env:DSP_DOTNET } else { 'dotnet' }
}

$game = Find-GameDir
$proj = Join-Path $root 'DSPQualitySystem.csproj'
if ($game) {
    Write-Host ('=== building ' + $proj + ' (GameDir=' + $game + ') ===')
    & $DspDotnet build $proj -c $Configuration -v minimal ('-p:GameDir=' + $game)
} else {
    Write-Host ('=== building ' + $proj + ' (no game install found; using tools\unity-refs if present) ===')
    & $DspDotnet build $proj -c $Configuration -v minimal
}

if ($LASTEXITCODE -ne 0) {
    Write-Host 'BUILD FAILED'
    exit 1
}
Write-Host 'BUILD OK'

if (-not $Deploy) {
    exit 0
}

if (-not $game) {
    Write-Host 'DEPLOY FAILED: game install not found (set DSP_GAME_DIR)'
    exit 1
}

$dll = Join-Path (Join-Path $root ('bin\' + $Configuration)) 'DSPQualitySystem.dll'
if (-not (Test-Path $dll)) {
    Write-Host ('DEPLOY FAILED: ' + $dll + ' not found')
    exit 1
}

$pluginDir = Join-Path $game 'BepInEx\plugins\DSPQualitySystem'
if (-not (Test-Path $pluginDir)) {
    New-Item -ItemType Directory -Force -Path $pluginDir | Out-Null
}

$target = Join-Path $pluginDir 'DSPQualitySystem.dll'
try {
    Copy-Item -Path $dll -Destination $target -Force
}
catch {
    Write-Host ('DEPLOY FAILED: ' + $_.Exception.Message + ' (is the game running?)')
    exit 1
}

$srcHash = (Get-FileHash -Path $dll -Algorithm SHA256).Hash
$dstHash = (Get-FileHash -Path $target -Algorithm SHA256).Hash
if ($srcHash -ne $dstHash) {
    Write-Host 'DEPLOY FAILED: hash mismatch after copy'
    exit 1
}

Write-Host ('DEPLOY OK -> ' + $target)
Write-Host ('DEPLOY SHA256 ' + $dstHash)
exit 0
