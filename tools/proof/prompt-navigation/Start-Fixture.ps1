#requires -Version 7.0
param(
    [Parameter(Mandatory)][string] $TerminalDirectory,
    [Parameter(Mandatory)][string] $RunDirectory,
    [switch] $Osc
)

$ErrorActionPreference = 'Stop'
$terminalRoot = [IO.Path]::GetFullPath($TerminalDirectory)
$runRoot = [IO.Path]::GetFullPath($RunDirectory)
$terminalExe = Join-Path $terminalRoot 'WindowsTerminal.exe'
$settingsPath = Join-Path $terminalRoot 'settings/settings.json'
if (-not (Test-Path -LiteralPath $terminalExe)) {
    throw 'Use a freshly extracted official Windows Terminal portable distribution.'
}
if (Test-Path -LiteralPath $settingsPath) {
    throw 'This launcher requires a fresh portable directory; existing settings will not be overwritten.'
}
if (Test-Path -LiteralPath $runRoot) {
    throw 'Choose a new run directory so earlier evidence is preserved.'
}

New-Item -ItemType Directory -Path $runRoot | Out-Null
$probeOutput = Join-Path $runRoot 'probe'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
dotnet build (Join-Path $PSScriptRoot 'PromptNavigation.Probe.csproj') -c Release --output $probeOutput
if ($LASTEXITCODE -ne 0) { throw 'The probe build failed.' }

New-Item -ItemType File -Path (Join-Path $terminalRoot '.portable') -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $terminalRoot 'settings') -Force | Out-Null
$profileId = '{91cbd0aa-8b94-4d1a-9617-8023866c6996}'
$settings = @{
    defaultProfile = $profileId
    initialCols = 105
    initialRows = 38
    copyOnSelect = $false
    profiles = @{
        defaults = @{
            historySize = 1000
            autoMarkPrompts = $true
            showMarksOnScrollbar = $true
            suppressApplicationTitle = $true
            closeOnExit = 'always'
            font = @{ face = 'Cascadia Mono'; size = 11 }
        }
        list = @(@{ guid = $profileId; name = 'Clici navigation proof' })
    }
    actions = @(
        @{ command = @{ action = 'scrollToMark'; direction = 'previous' }; keys = 'ctrl+up' },
        @{ command = @{ action = 'scrollToMark'; direction = 'next' }; keys = 'ctrl+down' }
    )
}
$settings | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $settingsPath
$mode = if ($Osc) { 'osc' } else { 'clean' }
$eventFile = Join-Path $runRoot 'input-events.jsonl'
$start = [Diagnostics.ProcessStartInfo]::new($terminalExe)
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
foreach ($argument in @('-w', 'new', '--title', "CLICI-PROOF-$mode", '--suppressApplicationTitle',
        '-d', $runRoot, 'dotnet', (Join-Path $probeOutput 'PromptNavigation.Probe.dll'), 'fixture', $mode, $eventFile)) {
    $start.ArgumentList.Add($argument)
}
$launcher = [Diagnostics.Process]::Start($start)
[pscustomobject]@{ RunDirectory = $runRoot; LauncherPid = $launcher.Id; Settings = $settingsPath; EventFile = $eventFile }
