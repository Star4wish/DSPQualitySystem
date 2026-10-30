param(
    [string]$Configuration = 'Release'
)

# Offline test runner for DSP Quality System (no game needed).
#   powershell -NoProfile -ExecutionPolicy Bypass -File tests\run-tests.ps1
#
# Optional environment:
#   DSP_TOOLS_DIR  folder with dspenv.ps1 (default: sibling DSP-MPD-runtime-debug\tools)
#   DSP_DOTNET     dotnet driver to use (default: from dspenv.ps1, otherwise "dotnet" on PATH)
#
# Success is reported with explicit markers: TEST PASS / TEST FAILED.
# The test program prints one [TEST] line per suite and "ALL PASS" / "FAILURES: n".

$ErrorActionPreference = 'Stop'

$root = Split-Path $PSScriptRoot -Parent

$tools = if ($env:DSP_TOOLS_DIR) { $env:DSP_TOOLS_DIR } else { Join-Path (Split-Path $root -Parent) 'DSP-MPD-runtime-debug\tools' }
$dspenv = Join-Path $tools 'dspenv.ps1'
if (Test-Path $dspenv) {
    . $dspenv
} else {
    Write-Host ('[env] no dspenv.ps1 at ' + $dspenv + ' - using dotnet from PATH')
    $DspDotnet = if ($env:DSP_DOTNET) { $env:DSP_DOTNET } else { 'dotnet' }
}

$proj = Join-Path $PSScriptRoot 'OfflineTests\OfflineTests.csproj'
Write-Host ('=== offline tests: ' + $proj + ' ===')
& $DspDotnet run --project $proj -c $Configuration -v minimal

if ($LASTEXITCODE -ne 0) {
    Write-Host 'TEST FAILED'
    exit 1
}
Write-Host 'TEST PASS'
exit 0
