# PowerShell port of tools/pack-r2modman.py (no usable python on this machine).
#
# Builds a Thunderstore / r2modman package with this layout:
#   manifest.json / icon.png / README.md / CHANGELOG.md
#   BepInEx/plugins/DSPQualitySystem/DSPQualitySystem.dll
#
# Usage:
#   powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\pack-r2modman.ps1
#   powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\pack-r2modman.ps1 -Version 0.18.0 -Dll .\bin\Release\DSPQualitySystem.dll
# Output: dist\DSPQualitySystem-<version>-r2modman.zip  (success marker: R2 PACKAGE OK)
# NOTE: keep this file pure ASCII (Windows PowerShell 5.1 reads BOM-less .ps1 as ANSI).

param(
    [string]$Version = '',
    [string]$Dll = ''
)

$ErrorActionPreference = 'Stop'

$root = Split-Path $PSScriptRoot -Parent
$srcDir = Join-Path $root 'tools\r2'
$csproj = Join-Path $root 'DSPQualitySystem.csproj'
$dllPath = if ($Dll) { (Resolve-Path $Dll).Path } else { Join-Path $root 'bin\Release\DSPQualitySystem.dll' }
$distDir = Join-Path $root 'dist'
$stageDir = Join-Path $distDir 'r2modman\DSPQualitySystem'

if (-not $Version) {
    $text = [IO.File]::ReadAllText($csproj, [Text.Encoding]::UTF8)
    $m = [regex]::Match($text, '<Version>([^<]+)</Version>')
    if (-not $m.Success) { Write-Host 'R2 PACKAGE FAILED: cannot find <Version> in csproj'; exit 1 }
    $Version = $m.Groups[1].Value.Trim()
}
if ($Version -notmatch '^\d+\.\d+\.\d+$') { Write-Host ('R2 PACKAGE FAILED: bad version ' + $Version); exit 1 }
if (-not (Test-Path $dllPath)) { Write-Host ('R2 PACKAGE FAILED: DLL not found: ' + $dllPath); exit 1 }

if (Test-Path $stageDir) { Remove-Item $stageDir -Recurse -Force }
$pluginDir = Join-Path $stageDir 'BepInEx\plugins\DSPQualitySystem'
New-Item -ItemType Directory -Force -Path $pluginDir | Out-Null

# manifest: keep template formatting, only substitute the version placeholder
$manifestPath = Join-Path $srcDir 'manifest.template.json'
$manifest = [IO.File]::ReadAllText($manifestPath, [Text.Encoding]::UTF8)
$manifest = $manifest.Replace('{{VERSION}}', $Version)
[IO.File]::WriteAllText((Join-Path $stageDir 'manifest.json'), $manifest, (New-Object Text.UTF8Encoding($false)))

$desc = ([regex]::Match($manifest, '"description"\s*:\s*"((?:[^"\\]|\\.)*)"')).Groups[1].Value
if ($desc.Length -gt 250) { Write-Host ('R2 PACKAGE FAILED: description too long (' + $desc.Length + ')'); exit 1 }

Copy-Item (Join-Path $srcDir 'icon.png') (Join-Path $stageDir 'icon.png') -Force
Copy-Item (Join-Path $srcDir 'README.md') (Join-Path $stageDir 'README.md') -Force
Copy-Item (Join-Path $srcDir 'CHANGELOG.md') (Join-Path $stageDir 'CHANGELOG.md') -Force
Copy-Item $dllPath (Join-Path $pluginDir 'DSPQualitySystem.dll') -Force

$zipPath = Join-Path $distDir ('DSPQualitySystem-' + $Version + '-r2modman.zip')
if (-not (Test-Path $distDir)) { New-Item -ItemType Directory -Force -Path $distDir | Out-Null }
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }

Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($stageDir, $zipPath, [IO.Compression.CompressionLevel]::Optimal, $false)

$zipHash = (Get-FileHash $zipPath -Algorithm SHA256).Hash
$dllHash = (Get-FileHash $dllPath -Algorithm SHA256).Hash
Write-Host ('R2 PACKAGE OK -> ' + $zipPath)
Write-Host ('  version        : ' + $Version)
Write-Host ('  size           : ' + (Get-Item $zipPath).Length + ' bytes')
Write-Host ('  sha256         : ' + $zipHash)
Write-Host ('  dll size/sha256: ' + (Get-Item $dllPath).Length + ' / ' + $dllHash)
Write-Host ('  description    : ' + $desc.Length + ' chars')
exit 0
