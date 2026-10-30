param(
    [string]$ConfigPath = ''
)

# Restore the test-time values in dsp.qualitysystem.cfg back to the shipped defaults.
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\restore-defaults.ps1
#
# Only three lines are rewritten in place (buildingChancePerPoint / minerChancePerPoint /
# maxUpgradeChance); every comment and the rest of the file is preserved byte-for-byte apart
# from those lines.
# Success markers: RESTORE OK / RESTORE FAILED.

$ErrorActionPreference = 'Stop'

if (-not $ConfigPath) {
    $game = $env:DSP_GAME_DIR
    if (-not $game) { $game = 'C:\Games\Steam\steamapps\common\Dyson Sphere Program' }
    $ConfigPath = Join-Path $game 'BepInEx\config\dsp.qualitysystem.cfg'
}

if (-not (Test-Path $ConfigPath)) {
    Write-Host ('RESTORE FAILED: config not found: ' + $ConfigPath)
    exit 1
}

$wanted = [ordered]@{
    'buildingChancePerPoint' = '1.5'
    'minerChancePerPoint'    = '2'
    'maxUpgradeChance'       = '60'
}

$lines = [IO.File]::ReadAllLines($ConfigPath, [Text.Encoding]::UTF8)
$found = @{}
$changed = 0

for ($i = 0; $i -lt $lines.Length; $i++) {
    $line = $lines[$i]
    foreach ($key in $wanted.Keys) {
        if ($line -match ('^\s*' + [regex]::Escape($key) + '\s*=')) {
            $found[$key] = $true
            $newLine = $key + ' = ' + $wanted[$key]
            if ($line -ne $newLine) {
                $lines[$i] = $newLine
                $changed++
            }
        }
    }
}

foreach ($key in $wanted.Keys) {
    if (-not $found.ContainsKey($key)) {
        Write-Host ('RESTORE FAILED: key not found in config: ' + $key)
        exit 1
    }
}

[IO.File]::WriteAllLines($ConfigPath, $lines, (New-Object Text.UTF8Encoding($false)))

Write-Host ('RESTORE OK -> ' + $ConfigPath)
Write-Host ('RESTORE CHANGED ' + $changed + ' line(s)')
foreach ($key in $wanted.Keys) {
    $verify = [IO.File]::ReadAllLines($ConfigPath, [Text.Encoding]::UTF8) | Where-Object { $_ -match ('^\s*' + [regex]::Escape($key) + '\s*=') } | Select-Object -First 1
    Write-Host ('RESTORE LINE ' + $verify)
}
Write-Host 'RESTORE NOTE: restart the game for the restored values to take effect.'

exit 0
