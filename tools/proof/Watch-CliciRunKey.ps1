<#
  Records every change to the HKCU Run value for clici, with a timestamp, to
  %LOCALAPPDATA%\clici\proof\runkey-watch.log.

  Purpose: steps 10/11 both depend on the Run value being in a known state at
  the moment of logon. A snapshot only proves what it was when the snapshot ran.
  If the value changes between the snapshot and the sign-out -- by a tray click,
  an installer, or the app itself -- the step result is meaningless and there is
  currently no way to tell after the fact. This closes that gap.

  Run it in its own window and leave it running while you set up and execute a
  step. It survives until you close it; it does NOT survive the sign-out, so
  start it again after signing back in to catch post-logon writes.
#>
# WARNING -- run this from an ordinary interactive shell.
# On 2026-08-29 an agent session's processes could not see the clici value under
# HKCU\Software\Microsoft\Windows\CurrentVersion\Run while an interactive shell
# on the same machine, user, SID and session could. Every registry read here was
# wrong in that context, and it reported "pre-state is correct" for step 11 twice
# when it was not. Compare-RunKeyView.ps1 detects the asymmetry.

[CmdletBinding()]
param(
    [int] $IntervalMilliseconds = 500
)

$ErrorActionPreference = 'Stop'

$key     = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$name    = 'clici'
$logDir  = Join-Path $env:LOCALAPPDATA 'clici\proof'
$logPath = Join-Path $logDir 'runkey-watch.log'
$null    = New-Item -ItemType Directory -Force -Path $logDir

function Read-Value {
    try {
        $v = (Get-Item $key).GetValue($name, $null)
        if ([string]::IsNullOrWhiteSpace($v)) { '(absent)' } else { $v }
    } catch { "(error: $($_.Exception.GetType().Name))" }
}

function Write-Entry([string] $text) {
    $line = "{0} {1}" -f (Get-Date).ToString('o'), $text
    Add-Content -Path $logPath -Value $line -Encoding UTF8
    Write-Host "  $line"
}

$logon = (Get-CimInstance Win32_LogonSession -Filter 'LogonType=2 OR LogonType=10 OR LogonType=11' -ErrorAction SilentlyContinue |
    Sort-Object StartTime -Descending | Select-Object -First 1).StartTime

Write-Host ''
Write-Host "  watching $key\$name"
Write-Host "  log      $logPath"
Write-Host "  logon    $logon"
Write-Host '  Ctrl+C to stop. Leave this running while you set up and run a step.'
Write-Host ''

Write-Entry "watch-start logon=$($logon.ToString('o')) value=$(Read-Value)"
$previous = Read-Value

try {
    while ($true) {
        Start-Sleep -Milliseconds $IntervalMilliseconds
        $current = Read-Value
        if ($current -ne $previous) {
            Write-Entry "CHANGED from=$previous to=$current"
            # Anything that started in the last 90s is a suspect for the write.
            $cutoff = (Get-Date).AddSeconds(-90)
            $recent = @(Get-Process -ErrorAction SilentlyContinue |
                Where-Object { $_.StartTime -and $_.StartTime -ge $cutoff } |
                Sort-Object StartTime |
                ForEach-Object { "$($_.ProcessName)#$($_.Id)@$($_.StartTime.ToString('HH:mm:ss'))" })
            if ($recent.Count -gt 0) {
                Write-Entry "  suspects (started within 90s): $($recent -join ', ')"
            } else {
                Write-Entry "  suspects: none started within 90s"
            }
            $clici = @(Get-Process clici -ErrorAction SilentlyContinue |
                ForEach-Object { "pid=$($_.Id) start=$($_.StartTime.ToString('HH:mm:ss')) path=$($_.Path)" })
            Write-Entry "  clici now: $(if ($clici.Count) { $clici -join ' | ' } else { 'none' })"
            $previous = $current
        }
    }
} finally {
    Write-Entry "watch-stop value=$(Read-Value)"
}
