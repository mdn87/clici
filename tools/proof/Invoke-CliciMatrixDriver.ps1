# Interactive driver for the runbook section 8/9 manual matrix. Run it inside
# the source terminal under test. For each fixture it displays the synthetic
# input, waits while the operator copies it with the terminal's normal Copy
# command, classifies the clipboard without printing it, and appends one JSON
# line (fixture, classification, clici decision metadata) to the run log.
# Compatible with Windows PowerShell 5.1 so it can run from cmd and legacy hosts.
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$Source,

    [string[]]$FixtureId = @('FX-01', 'FX-02', 'FX-03', 'FX-04'),

    # Every mode other than Normal expects clici to leave the item unchanged.
    [ValidateSet('Normal', 'Paused', 'Disabled', 'Disallowed', 'Excluded')]
    [string]$Mode = 'Normal',

    # Do not display the fixture; for sources that render it themselves
    # (for example an agent TUI asked to run ShowInput).
    [switch]$NoShow,

    [string]$LogPath,

    [string]$FixturePath
)

$ErrorActionPreference = 'Stop'

# `-File` passes "FX-01,FX-02" as one string; accept either form.
$FixtureId = @($FixtureId | ForEach-Object { $_ -split ',' } | Where-Object { $_ })

# Windows PowerShell 5.1 leaves $PSScriptRoot empty in parameter defaults.
if (-not $FixturePath) {
    $FixturePath = Join-Path $PSScriptRoot 'fixtures.json'
}

if ([System.Threading.Thread]::CurrentThread.GetApartmentState() -ne 'STA') {
    throw 'Run this driver in an STA process (pwsh -Sta, or Windows PowerShell).'
}

Add-Type -AssemblyName System.Windows.Forms

if (-not $LogPath) {
    $runs = Join-Path $PSScriptRoot '..\..\artifacts\v0.1-proof'
    # Only runbook run IDs (yyyyMMdd-HHmmss); other folders sort after them.
    $latest = Get-ChildItem -LiteralPath $runs -Directory |
        Where-Object { $_.Name -match '^\d{8}-\d{6}$' } |
        Sort-Object Name |
        Select-Object -Last 1
    if ($null -eq $latest) {
        throw 'No run directory under artifacts\v0.1-proof; pass -LogPath.'
    }
    $LogPath = Join-Path $latest.FullName 'diagnostics\matrix.jsonl'
}

$cliciLog = Join-Path $env:LOCALAPPDATA 'clici\clici.log'
# Windows PowerShell 5.1 reads ANSI by default and emits a JSON array as one
# object; force UTF-8 and unroll it.
$fixtures = @(Get-Content -Raw -Encoding UTF8 -LiteralPath $FixturePath |
    ConvertFrom-Json |
    ForEach-Object { $_ })

function Get-DecisionsSince {
    param([DateTimeOffset]$Since)

    if (-not (Test-Path -LiteralPath $cliciLog)) {
        return @()
    }

    # The diagnostic log carries metadata only (status, process, counts).
    $lines = Get-Content -LiteralPath $cliciLog -Tail 40
    $result = @()
    foreach ($line in $lines) {
        $stamp = ($line -split ' ', 2)[0]
        $parsed = [DateTimeOffset]::MinValue
        if ([DateTimeOffset]::TryParse($stamp, [ref]$parsed) -and $parsed -ge $Since) {
            $result += ($line -split ' ', 2)[1]
        }
    }

    return $result
}

# Describes where two synthetic strings first diverge using positions and code
# units only, so a terminal's copy transformation can be identified without
# recording clipboard text.
function Get-FirstDifference {
    param([string]$Actual, [string]$Wanted)

    if ($null -eq $Actual) {
        return 'clipboard had no text'
    }

    $limit = [Math]::Min($Actual.Length, $Wanted.Length)
    $index = 0
    while ($index -lt $limit -and $Actual[$index] -eq $Wanted[$index]) {
        $index++
    }

    $got = if ($index -lt $Actual.Length) { 'U+{0:X4}' -f [int]$Actual[$index] } else { 'end' }
    $want = if ($index -lt $Wanted.Length) { 'U+{0:X4}' -f [int]$Wanted[$index] } else { 'end' }
    return ('at {0}: got {1} want {2}; length {3} vs {4}' -f $index, $got, $want, $Actual.Length, $Wanted.Length)
}

foreach ($id in $FixtureId) {
    $fixture = @($fixtures | Where-Object { $_.id -eq $id })
    if ($fixture.Count -ne 1) {
        throw "Fixture '$id' was not found exactly once."
    }
    $fixture = $fixture[0]

    $expected = if ($Mode -ne 'Normal') {
        if ($fixture.expectedStatus -eq 'Normalized') { 'MatchesInput' } else { 'MatchesInputAndExpected' }
    }
    elseif ($fixture.expectedStatus -eq 'Normalized') {
        'MatchesExpected'
    }
    else {
        'MatchesInputAndExpected'
    }

    Write-Host ''
    Write-Host "=== $id [$Source/$Mode] expect $expected ===" -ForegroundColor Cyan
    if (-not $NoShow) {
        Write-Host 'Select exactly the text between the rules, then Copy:'
        Write-Host '----------------------------------------'
        # The default console code page (437) renders characters such as U+2615
        # as '?', so the terminal would show and copy different text.
        $previousEncoding = [Console]::OutputEncoding
        [Console]::OutputEncoding = New-Object System.Text.UTF8Encoding $false
        try {
            [Console]::Out.Write([string]$fixture.input)
            [Console]::Out.WriteLine()
        }
        finally {
            [Console]::OutputEncoding = $previousEncoding
        }
        Write-Host '----------------------------------------'
    }

    $shownAt = [DateTimeOffset]::Now
    $answer = Read-Host 'Press Enter after copying (s = skip, q = quit)'
    if ($answer -eq 'q') {
        break
    }
    if ($answer -eq 's') {
        continue
    }

    Start-Sleep -Milliseconds 250

    $actual = $null
    for ($attempt = 0; $attempt -lt 10 -and $null -eq $actual; $attempt++) {
        try {
            $actual = [System.Windows.Forms.Clipboard]::GetText(
                [System.Windows.Forms.TextDataFormat]::UnicodeText)
        }
        catch [System.Runtime.InteropServices.ExternalException] {
            Start-Sleep -Milliseconds 50
        }
    }

    $matchesInput = [string]::Equals($actual, [string]$fixture.input, [StringComparison]::Ordinal)
    $matchesExpected = [string]::Equals($actual, [string]$fixture.expectedOutput, [StringComparison]::Ordinal)
    $classification = if ($matchesInput -and $matchesExpected) {
        'MatchesInputAndExpected'
    }
    elseif ($matchesExpected) {
        'MatchesExpected'
    }
    elseif ($matchesInput) {
        'MatchesInput'
    }
    else {
        'UnexpectedContent'
    }

    # Decisions written while the prompt was waiting belong to this copy; the
    # last one is the final outcome.
    # A normalization is followed by clici skipping its own write, so prefer the
    # last decision line and fall back to a skip event.
    $recent = @(Get-DecisionsSince -Since $shownAt)
    $decisions = @($recent | Where-Object { $_ -like 'decision *' })
    if ($decisions.Count -eq 0) {
        $decisions = @($recent | Where-Object { $_ -like 'event name=skipped*' })
    }
    $decision = if ($decisions.Count -gt 0) { $decisions[-1] } else { $null }

    $record = [pscustomobject]@{
        Time = [DateTimeOffset]::Now.ToString('O')
        Source = $Source
        Mode = $Mode
        FixtureId = $id
        Expected = $expected
        Classification = $classification
        Pass = $classification -eq $expected
        Decision = $decision
        VersusInput = $null
        VersusExpected = $null
    }
    if (-not $record.Pass) {
        $record.VersusInput = Get-FirstDifference -Actual $actual -Wanted ([string]$fixture.input)
        $record.VersusExpected = Get-FirstDifference -Actual $actual -Wanted ([string]$fixture.expectedOutput)
    }
    $record | ConvertTo-Json -Compress | Add-Content -LiteralPath $LogPath -Encoding utf8

    $color = if ($record.Pass) { 'Green' } else { 'Red' }
    Write-Host ("{0}: {1} (expected {2}) | {3}" -f $id, $classification, $expected, $decision) -ForegroundColor $color
    if (-not $record.Pass) {
        Write-Host ("  vs input: {0}" -f $record.VersusInput) -ForegroundColor $color
        Write-Host ("  vs expected: {0}" -f $record.VersusExpected) -ForegroundColor $color
    }
}

Write-Host ''
Write-Host "Logged to $LogPath"
