<#
  clici installer runbook - auto-start behaviour proof (runbook steps 10 and 11).

    Step 10: auto-start ENABLED  -> clici MUST start by itself after sign-in.
    Step 11: auto-start DISABLED -> clici must NOT start by itself after sign-in.

  Usage:
    # before signing out
    .\Test-LifecycleStep.ps1 -Step 11 -Snapshot
    # sign out, sign back in, then
    .\Test-LifecycleStep.ps1 -Step 11
#>
[CmdletBinding()]
param(
    [ValidateSet(10, 11)] [int] $Step = 11,
    [switch] $Snapshot,
    [switch] $NoPause
)

$ErrorActionPreference = 'Stop'

$RunKeyPath   = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$RunKeyName   = 'clici'
$LogPath      = Join-Path $env:LOCALAPPDATA 'clici\clici.log'
$SnapshotPath = Join-Path $env:LOCALAPPDATA "clici\proof\step$Step-snapshot.json"

function Get-RunKeyValue {
    try { (Get-Item $RunKeyPath).GetValue($RunKeyName, $null) } catch { $null }
}

function Get-StartedCount {
    if (-not (Test-Path $LogPath)) { return 0 }
    @(Select-String -Path $LogPath -SimpleMatch 'event name=started' -ErrorAction SilentlyContinue).Count
}

function Get-LastLogLine {
    if (-not (Test-Path $LogPath)) { return '(no log file)' }
    $tail = Get-Content $LogPath -Tail 1 -ErrorAction SilentlyContinue
    if ($tail) { $tail } else { '(log file empty)' }
}

function Get-BuildVersion {
    if (-not (Test-Path $LogPath)) { return '(unknown)' }
    $line = Select-String -Path $LogPath -Pattern 'event name=started version=(\S+)' |
        Select-Object -Last 1
    if ($line) { $line.Matches[0].Groups[1].Value } else { '(unknown)' }
}

# Start of the interactive session we are running in, used to tell a real
# sign-out/sign-in from an application restart. A clici.exe that started after
# the snapshot proves nothing on its own -- the app can be stopped and relaunched
# by hand in seconds. It has to have started after the LOGON.
#
# Primary source is the LSA interactive logon session; explorer.exe's start time
# is the fallback. Both are cross-checked against the boot time.
function Get-LogonTime {
    $sid = (Get-Process -Id $PID).SessionId

    $sessions = @(
        Get-CimInstance Win32_LogonSession -Filter 'LogonType=2 OR LogonType=10 OR LogonType=11' -ErrorAction SilentlyContinue |
            Where-Object { $_.StartTime } |
            Sort-Object StartTime -Descending
    )
    if ($sessions.Count -gt 0) { return $sessions[0].StartTime }

    $shell = Get-Process -Name explorer -ErrorAction SilentlyContinue |
        Where-Object { $_.SessionId -eq $sid } |
        Sort-Object StartTime |
        Select-Object -First 1
    if ($shell) { return $shell.StartTime }

    return $null
}

function Get-SessionKind {
    try {
        $line = (quser 2>$null | Select-String -Pattern "^\s*>?\s*$env:USERNAME\s")
        if ($line -and $line.ToString() -match 'rdp') { return 'RDP' }
        if ($line) { return 'console' }
    } catch { }
    'unknown'
}

function Get-CliciProcesses {
    Get-Process -Name clici -ErrorAction SilentlyContinue |
        ForEach-Object {
            [pscustomobject]@{ Id = $_.Id; StartTime = $_.StartTime; Path = $_.Path }
        }
}

# ---------------------------------------------------------------- snapshot ---
if ($Snapshot) {
    $runValue = Get-RunKeyValue
    $expectSet = ($Step -eq 10)
    $isSet = -not [string]::IsNullOrWhiteSpace($runValue)

    $null = New-Item -ItemType Directory -Force -Path (Split-Path $SnapshotPath)
    [pscustomobject]@{
        step         = $Step
        takenAt      = (Get-Date).ToString('o')
        build        = Get-BuildVersion
        runKey       = $runValue
        startedCount = Get-StartedCount
        pids         = @((Get-CliciProcesses).Id)
    } | ConvertTo-Json | Set-Content -Path $SnapshotPath -Encoding UTF8

    Write-Host ''
    Write-Host "  snapshot for STEP $Step written"
    Write-Host "  file               : $SnapshotPath"
    Write-Host "  build              : $(Get-BuildVersion)"
    Write-Host "  Run key            : $(if ($isSet) { $runValue } else { '(absent)' })"
    Write-Host "  'started' entries  : $(Get-StartedCount)"
    Write-Host "  clici instances    : $(@(Get-CliciProcesses).Count)"
    Write-Host ''
    if ($isSet -ne $expectSet) {
        Write-Host "  !! WRONG PRE-STATE for step $Step." -ForegroundColor Yellow
        if ($expectSet) {
            Write-Host "     Step 10 needs auto-start ON  - tick 'Start with Windows' in the tray menu." -ForegroundColor Yellow
        } else {
            Write-Host "     Step 11 needs auto-start OFF - untick 'Start with Windows' in the tray menu." -ForegroundColor Yellow
        }
        Write-Host '     Fix it, then re-run the snapshot.' -ForegroundColor Yellow
    } else {
        Write-Host '  Pre-state is correct. Now sign out, sign back in, and re-run this'
        Write-Host "  script without -Snapshot to check step $Step."
    }
    Write-Host ''
    if (-not $NoPause) { Read-Host 'Press Enter to close' | Out-Null }
    return
}

# ------------------------------------------------------------------ verify ---
if (-not (Test-Path $SnapshotPath)) {
    throw "No snapshot found at $SnapshotPath. Run with -Snapshot before signing out."
}

$snap      = Get-Content $SnapshotPath -Raw | ConvertFrom-Json
$snapTaken = [datetime]::Parse($snap.takenAt)
$logon     = Get-LogonTime
$realSignOut = ($null -ne $logon) -and ($logon -gt $snapTaken)

$runValue  = Get-RunKeyValue
$runIsSet  = -not [string]::IsNullOrWhiteSpace($runValue)
$procs     = @(Get-CliciProcesses)
$newStarts = (Get-StartedCount) - [int]$snap.startedCount

$autoStarted = @($procs | Where-Object { $null -ne $logon -and $_.StartTime -ge $logon })

$title   = if ($Step -eq 10) { 'STEP 10 - autostart ON, clici must start by itself' }
           else              { 'STEP 11 - autostart OFF, clici must NOT start by itself' }

$failures = New-Object System.Collections.Generic.List[string]
if (-not $realSignOut) {
    $failures.Add('no real sign-out/sign-in happened after the snapshot was taken')
}
if ($Step -eq 10) {
    if (-not $runIsSet)               { $failures.Add('Run key value is missing - auto-start was not enabled') }
    if ($autoStarted.Count -lt 1)     { $failures.Add('no clici process started after logon') }
    if ($newStarts -lt 1)             { $failures.Add("no new 'started' log entry since the snapshot") }
} else {
    if ($runIsSet)                    { $failures.Add("Run key value is still present: $runValue") }
    if ($autoStarted.Count -gt 0)     { $failures.Add("clici started anyway (pid $($autoStarted.Id -join ', '))") }
    if ($newStarts -gt 0)             { $failures.Add("$newStarts new 'started' log entry/entries since the snapshot") }
}

Write-Host ''
Write-Host "  step being checked : $title"
Write-Host "  snapshot taken     : $snapTaken"
Write-Host "  snapshot build     : $($snap.build)"
Write-Host "  logon at           : $(if ($logon) { $logon } else { '(unknown)' })"
Write-Host "  session kind       : $(Get-SessionKind)"
Write-Host "  real sign-out?     : $(if ($realSignOut) { 'YES' } else { 'NO' })"
Write-Host "  Run key            : $(if ($runIsSet) { $runValue } else { '(absent)' })"
Write-Host "  clici instances    : $($procs.Count)"
foreach ($p in $procs) {
    $after = ($null -ne $logon) -and ($p.StartTime -ge $logon)
    Write-Host "      pid $($p.Id)  started $($p.StartTime.ToString('HH:mm:ss'))  after logon: $after"
    Write-Host "      path $($p.Path)"
}
Write-Host "  new 'started' log entries since snapshot : $newStarts"
Write-Host "  last log line      : $(Get-LastLogLine)"
Write-Host ''

if ($failures.Count -eq 0) {
    Write-Host '  ############   P A S S   ############' -ForegroundColor Green
} else {
    Write-Host '  ############   F A I L   ############' -ForegroundColor Red
    foreach ($f in $failures) { Write-Host "  - $f" -ForegroundColor Red }
}
Write-Host ''
if (-not $NoPause) { Read-Host 'Press Enter to close' | Out-Null }
exit $(if ($failures.Count -eq 0) { 0 } else { 1 })
