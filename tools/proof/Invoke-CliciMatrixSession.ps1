# Guided Windows Terminal session for runbook sections 8.1 and 8.2 plus
# terminal re-copy characterization. Tells the operator which tray action to
# take and when, and runs Invoke-CliciMatrixDriver.ps1 between them.
[CmdletBinding()]
param(
    [string]$Source = 'WT-pwsh'
)

$ErrorActionPreference = 'Stop'

if ([System.Threading.Thread]::CurrentThread.GetApartmentState() -ne 'STA') {
    throw 'Run this session in an STA process: pwsh -Sta -File <script>'
}

$driver = Join-Path $PSScriptRoot 'Invoke-CliciMatrixDriver.ps1'
$fixtureHelper = Join-Path $PSScriptRoot 'Invoke-CliciFixtureProof.ps1'
$matrixLog = Join-Path (
    Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot '..\..\artifacts\v0.1-proof') -Directory |
        Where-Object { $_.Name -match '^\d{8}-\d{6}$' } |
        Sort-Object Name |
        Select-Object -Last 1
).FullName 'diagnostics\matrix.jsonl'

function Wait-Tray {
    param([string]$Instruction)

    Write-Host ''
    Write-Host ">>> TRAY: $Instruction" -ForegroundColor Yellow
    [void](Read-Host 'Press Enter when done')
}

Write-Host 'Make sure clici is Enabled and not paused before starting.' -ForegroundColor Yellow

& $driver -Source $Source -FixtureId 'FX-06', 'FX-07', 'FX-08'

Wait-Tray 'click "Pause normalization".'
& $driver -Source $Source -Mode Paused -FixtureId 'FX-06', 'FX-07', 'FX-08', 'FX-01'

Wait-Tray 'click "Resume normalization".'
& $driver -Source $Source -FixtureId 'FX-01'

Wait-Tray 'click "Enabled" to turn it OFF.'
& $driver -Source $Source -Mode Disabled -FixtureId 'FX-01'

Wait-Tray 'click "Enabled" to turn it back ON.'
& $driver -Source $Source -FixtureId 'FX-01'

Write-Host ''
Write-Host '=== 8.1 byte-identical FX-03 (no copying needed) ===' -ForegroundColor Cyan
& $fixtureHelper -Action SetExpected -FixtureId 'FX-03'
Start-Sleep -Milliseconds 250
$result = & $fixtureHelper -Action Classify -FixtureId 'FX-03'
$color = if ($result.Classification -eq 'MatchesExpected') { 'Green' } else { 'Red' }
Write-Host ("8.1 FX-03: {0} (must be MatchesExpected)" -f $result.Classification) -ForegroundColor $color
[pscustomobject]@{
    Time = [DateTimeOffset]::Now.ToString('O')
    Source = $Source
    Mode = 'SetExpected'
    FixtureId = 'FX-03'
    Expected = 'MatchesExpected'
    Classification = $result.Classification
    Pass = $result.Classification -eq 'MatchesExpected'
} | ConvertTo-Json -Compress | Add-Content -LiteralPath $matrixLog -Encoding utf8

Write-Host ''
Write-Host 'Session complete. Tell Claude you are done.' -ForegroundColor Green
