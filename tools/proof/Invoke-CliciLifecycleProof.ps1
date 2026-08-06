[CmdletBinding()]
param(
    [string]$ExecutablePath = (
        Join-Path $PSScriptRoot '..\..\src\clici.App\bin\Release\net10.0-windows\clici.exe'
    ),
    [int]$TimeoutMilliseconds = 5000
)

$ErrorActionPreference = 'Stop'

Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

public static class CliciWindowProbe
{
    private delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr window, StringBuilder text, int maximum);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    public static uint[] FindListenerProcessIds()
    {
        var processIds = new List<uint>();

        EnumWindows((window, _) =>
        {
            var text = new StringBuilder(256);
            GetWindowText(window, text, text.Capacity);

            if (string.Equals(
                text.ToString(),
                "clici clipboard listener",
                StringComparison.Ordinal))
            {
                GetWindowThreadProcessId(window, out var processId);
                processIds.Add(processId);
            }

            return true;
        }, IntPtr.Zero);

        return processIds.ToArray();
    }
}
'@

function Get-CliciListenerProcessIds {
    @([CliciWindowProbe]::FindListenerProcessIds())
}

function Wait-ForListenerState {
    param(
        [int]$ExpectedCount,
        [int]$ExpectedOwnerProcessId = 0
    )

    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()

    while ($stopwatch.ElapsedMilliseconds -lt $TimeoutMilliseconds) {
        $owners = @(Get-CliciListenerProcessIds)
        $ownerMatches = (
            $ExpectedOwnerProcessId -eq 0 -or
            ($owners.Count -eq 1 -and $owners[0] -eq $ExpectedOwnerProcessId)
        )

        if ($owners.Count -eq $ExpectedCount -and $ownerMatches) {
            return $owners
        }

        Start-Sleep -Milliseconds 50
    }

    $observed = @(Get-CliciListenerProcessIds)
    throw "Timed out waiting for $ExpectedCount listener(s); observed owners: $($observed -join ',')."
}

function Wait-ForTrayExit {
    param(
        [System.Diagnostics.Process]$Process,
        [string]$Prompt
    )

    [void](Read-Host $Prompt)
    if (-not $Process.WaitForExit($TimeoutMilliseconds)) {
        throw "clici did not exit within $TimeoutMilliseconds ms after the tray Exit action."
    }

    [void](Wait-ForListenerState -ExpectedCount 0)
}

$resolvedExecutable = (Resolve-Path -LiteralPath $ExecutablePath).Path
$existingProcesses = @(Get-Process -Name 'clici' -ErrorAction SilentlyContinue)
if ($existingProcesses.Count -ne 0) {
    throw 'Exit the existing clici instance through its tray menu before running this proof.'
}

$primary = $null
$reacquired = $null

try {
    $primary = Start-Process -FilePath $resolvedExecutable -WindowStyle Hidden -PassThru
    [void](Wait-ForListenerState -ExpectedCount 1 -ExpectedOwnerProcessId $primary.Id)

    $secondary = Start-Process -FilePath $resolvedExecutable -WindowStyle Hidden -PassThru
    if (-not $secondary.WaitForExit($TimeoutMilliseconds)) {
        throw 'The second clici process did not exit within the bounded wait.'
    }
    if ($secondary.ExitCode -ne 0) {
        throw "The second clici process exited with code $($secondary.ExitCode)."
    }
    if ($primary.HasExited) {
        throw 'The primary clici process exited during the second-instance check.'
    }

    $runningProcesses = @(Get-Process -Name 'clici' -ErrorAction SilentlyContinue)
    if ($runningProcesses.Count -ne 1 -or $runningProcesses[0].Id -ne $primary.Id) {
        throw 'The second-instance check did not leave exactly the primary process running.'
    }
    [void](Wait-ForListenerState -ExpectedCount 1 -ExpectedOwnerProcessId $primary.Id)

    Wait-ForTrayExit `
        -Process $primary `
        -Prompt 'Choose Exit from the primary clici tray menu, then press Enter'

    $reacquired = Start-Process -FilePath $resolvedExecutable -WindowStyle Hidden -PassThru
    [void](Wait-ForListenerState -ExpectedCount 1 -ExpectedOwnerProcessId $reacquired.Id)

    Wait-ForTrayExit `
        -Process $reacquired `
        -Prompt 'Choose Exit from the reacquired clici tray menu, then press Enter'

    if (@(Get-Process -Name 'clici' -ErrorAction SilentlyContinue).Count -ne 0) {
        throw 'A clici process remained after the final tray Exit action.'
    }

    [pscustomobject]@{
        Result = 'Pass'
        PrimaryProcessId = $primary.Id
        SecondaryExitCode = $secondary.ExitCode
        ReacquiredProcessId = $reacquired.Id
        FinalListenerCount = @(Get-CliciListenerProcessIds).Count
    }
}
finally {
    foreach ($startedProcess in @($primary, $reacquired)) {
        if ($null -ne $startedProcess -and -not $startedProcess.HasExited) {
            Stop-Process -Id $startedProcess.Id -Force -ErrorAction SilentlyContinue
        }
    }
}
