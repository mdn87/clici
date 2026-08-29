<#
  clici installer runbook - fresh install and tray auto-start toggle
  (runbook Build 1-2 and steps 1-9).

  Steps 1, 3, 4, 5, 6, 8 and 9 are machine-verified here. Steps 2 and 7 are
  visual and are recorded as operator observations, labelled as such -- a y/n
  from a person is not the same class of evidence as an install log, and the
  result file keeps the two apart.

  Usage (double-click, or run from an ordinary terminal):
    .\Test-LifecycleStep1-9.ps1

  WARNING -- run this from an ordinary interactive shell.
  Steps 6, 8 and 9 read HKCU\...\Run\clici. On 2026-08-29 an agent session's
  processes could not see that value while an interactive shell on the same
  machine, user, SID and session could, and it produced two false step-11
  results. This script cross-checks the step 6 registry read against the
  installer's own /LOG output and shouts when the two disagree.
#>
[CmdletBinding()]
param(
    [string] $Setup = (Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) 'artifacts\installer\clici-0.1.0-win-x64-setup.exe'),
    [switch] $NoPause
)

$ErrorActionPreference = 'Stop'

$RunKey     = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$AppExe     = Join-Path $env:LOCALAPPDATA 'Programs\clici\clici.exe'
$Shortcut   = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\clici.lnk'
$InstallLog = Join-Path $env:TEMP 'clici-steps1-9-install.log'
$ResultPath = Join-Path $env:LOCALAPPDATA 'clici\proof\steps1-9-result.json'
$Expected   = '"' + $AppExe + '"'

function Get-RunValue { try { (Get-Item $RunKey).GetValue('clici', $null) } catch { $null } }
function Get-RunValueCount { try { @((Get-Item $RunKey).GetValueNames()).Count } catch { -1 } }
function Get-Arp {
    Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\*' -ErrorAction SilentlyContinue |
        Where-Object { $_.DisplayName -eq 'clici' } | Select-Object -First 1
}
function Ask([string] $question) {
    while ($true) {
        $a = (Read-Host "  $question [y/n]").Trim().ToLower()
        if ($a -eq 'y') { return $true }
        if ($a -eq 'n') { return $false }
    }
}

$results = [ordered]@{}
function Record([string] $step, [string] $title, [bool] $pass, [string] $evidence, [string] $kind) {
    $results[$step] = [ordered]@{ title = $title; pass = $pass; evidence = $evidence; kind = $kind }
    $tag = if ($pass) { 'PASS' } else { 'FAIL' }
    $col = if ($pass) { 'Green' } else { 'Red' }
    Write-Host ("  {0,-4} step {1,-2} {2}" -f $tag, $step, $title) -ForegroundColor $col
    Write-Host ("       {0}" -f $evidence) -ForegroundColor DarkGray
}

Write-Host ''
Write-Host '  clici installer runbook - steps 1-9' -ForegroundColor Cyan
Write-Host "  setup : $Setup"
Write-Host ''

if (-not (Test-Path $Setup)) { throw "Installer not found at $Setup. Run tools/Build-Installer.ps1 first." }
if (Test-Path $AppExe)       { throw "clici is still installed at $AppExe. Uninstall first -- step 1 is a FRESH install." }

# ---------------------------------------------------------------- step 1 ----
Write-Host '  STEP 1 - the installer wizard is about to open.' -ForegroundColor Yellow
Write-Host '    - Leave "Start clici when I sign in" CHECKED.'
Write-Host '    - Finish the wizard and let clici launch (leave "Launch clici now" checked).'
Write-Host ''
Read-Host '  Press Enter to launch setup' | Out-Null

$proc = Start-Process -FilePath $Setup -ArgumentList "/LOG=$InstallLog" -Wait -PassThru
Start-Sleep -Seconds 4
$procs = @(Get-Process -Name clici -ErrorAction SilentlyContinue)
$logText = if (Test-Path $InstallLog) { Get-Content $InstallLog -Raw } else { '' }
$logSilent = $logText -match '/VERYSILENT|/SILENT'
$step1 = ($proc.ExitCode -eq 0) -and (Test-Path $AppExe) -and ($procs.Count -ge 1) -and (-not $logSilent)
Record '1' 'wizard install, clici launches' $step1 `
    ("setup exit $($proc.ExitCode); wizard (not silent): $(-not $logSilent); clici.exe present: $(Test-Path $AppExe); clici processes after: $($procs.Count)") 'machine'

Write-Host ''
Record '2' 'tray icon present in the notification area' (Ask 'Do you see the clici icon in the notification area?') `
    'operator observation at the machine' 'operator'

# ------------------------------------------------------------- steps 3-5 ----
Write-Host ''
$fileVer = if (Test-Path $AppExe) { (Get-Item $AppExe).VersionInfo.ProductVersion } else { '(missing)' }
Record '3' 'install location' (Test-Path $AppExe) "$AppExe exists; ProductVersion $fileVer" 'machine'
Record '4' 'Start Menu shortcut' (Test-Path $Shortcut) "$Shortcut exists" 'machine'
$arp = Get-Arp
Record '5' 'Add or remove programs entry' ($null -ne $arp -and -not [string]::IsNullOrWhiteSpace($arp.DisplayVersion)) `
    ("DisplayName '$($arp.DisplayName)', DisplayVersion '$($arp.DisplayVersion)'") 'machine'

# --------------------------------------------------------------- step 6 ----
# Two independent sources: the installer's own log, and this shell's read.
$logWroteRun = $logText -match [regex]::Escape('Software\Microsoft\Windows\CurrentVersion\Run')
$run6   = Get-RunValue
$count6 = Get-RunValueCount
$read6  = ($run6 -eq $Expected)
$disagree6 = ($logWroteRun -and -not $read6)
Record '6' 'Run value equals the quoted installed path' ($read6 -and $logWroteRun) `
    ("install log records a Run write: $logWroteRun; this shell reads: $(if ($run6) { $run6 } else { '(absent)' }); expected: $Expected; Run value count: $count6") 'machine'
if ($disagree6) {
    Write-Host ''
    Write-Host '  !! The install log says the Run value was written and this shell cannot see it.' -ForegroundColor Red
    Write-Host '     That is the known agent-session registry blindness. You are NOT in an ordinary' -ForegroundColor Red
    Write-Host '     interactive shell, or the same fault has reappeared. Steps 6, 8 and 9 are void.' -ForegroundColor Red
    Write-Host '     Re-run from a normal terminal; compare with tools/proof/Compare-RunKeyView.ps1.' -ForegroundColor Red
}

# ------------------------------------------------------------ steps 7-9 ----
Write-Host ''
Record '7' 'tray menu shows "Start with Windows" checked' `
    (Ask 'Open the tray menu. Is "Start with Windows" CHECKED?') `
    'operator observation; must agree with step 6' 'operator'

Write-Host ''
Write-Host '  STEP 8 - now UNCHECK "Start with Windows" in the tray menu.' -ForegroundColor Yellow
Read-Host '  Press Enter once you have unchecked it' | Out-Null
Start-Sleep -Milliseconds 500
$run8 = Get-RunValue
Record '8' 'unchecking removes the Run value' ([string]::IsNullOrWhiteSpace($run8)) `
    ("after unchecking, this shell reads: $(if ($run8) { $run8 } else { '(absent)' }); Run value count: $(Get-RunValueCount)") 'machine'

Write-Host ''
Write-Host '  STEP 9 - now RE-CHECK "Start with Windows".' -ForegroundColor Yellow
Read-Host '  Press Enter once you have re-checked it' | Out-Null
Start-Sleep -Milliseconds 500
$run9 = Get-RunValue
Record '9' 'rechecking writes the quoted path back' ($run9 -eq $Expected) `
    ("after rechecking, this shell reads: $(if ($run9) { $run9 } else { '(absent)' }); expected: $Expected; Run value count: $(Get-RunValueCount)") 'machine'

# ---------------------------------------------------------------- output ----
$null = New-Item -ItemType Directory -Force -Path (Split-Path $ResultPath)
[ordered]@{
    ranAt        = (Get-Date).ToString('o')
    setup        = $Setup
    setupBuilt   = (Get-Item $Setup).LastWriteTime.ToString('o')
    installedVer = $fileVer
    sessionId    = (Get-Process -Id $PID).SessionId
    installLog   = $InstallLog
    expectedRun  = $Expected
    steps        = $results
} | ConvertTo-Json -Depth 6 | Set-Content -Path $ResultPath -Encoding UTF8

$failed = @($results.Keys | Where-Object { -not $results[$_].pass })
Write-Host ''
if ($failed.Count -eq 0) {
    Write-Host '  ############   S T E P S  1 - 9   P A S S   ############' -ForegroundColor Green
} else {
    Write-Host "  ############   F A I L  (steps $($failed -join ', '))   ############" -ForegroundColor Red
}
Write-Host ''
Write-Host "  result file : $ResultPath"
Write-Host "  install log : $InstallLog"
Write-Host ''
if (-not $NoPause) { Read-Host 'Press Enter to close' | Out-Null }
