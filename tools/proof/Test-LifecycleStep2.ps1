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
            if (string.Equals(text.ToString(), "clici clipboard listener", StringComparison.Ordinal))
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

$exePath = Resolve-Path 'src/clici.App/bin/Release/net10.0-windows/clici.exe'

# 1. Verify primary exited and no listeners remain
$procs = Get-Process -Name 'clici' -ErrorAction SilentlyContinue
if ($procs) {
    throw "clici process is still running (PID $($procs[0].Id)). Did you click Exit in the tray menu?"
}
$listeners = [CliciWindowProbe]::FindListenerProcessIds()
if ($listeners.Count -ne 0) {
    throw "Orphan listener window detected."
}
Write-Host "Primary tray exit verified cleanly (0 processes, 0 listener windows)."

# 2. Reacquire instance
$reacquired = Start-Process -FilePath $exePath -WindowStyle Hidden -PassThru
Write-Host "Reacquired clici process started with PID $($reacquired.Id)"

# 3. Wait for listener
$sw = [System.Diagnostics.Stopwatch]::StartNew()
$listenerPids = @()
while ($sw.ElapsedMilliseconds -lt 5000) {
    $listenerPids = [CliciWindowProbe]::FindListenerProcessIds()
    if ($listenerPids.Count -eq 1 -and $listenerPids[0] -eq $reacquired.Id) {
        break
    }
    Start-Sleep -Milliseconds 100
}
if ($listenerPids.Count -ne 1 -or $listenerPids[0] -ne $reacquired.Id) {
    throw "Reacquired listener window not found for PID $($reacquired.Id)"
}
Write-Host "Reacquired listener window verified (HWND owned by PID $($reacquired.Id))."
Write-Host "Step 2 complete. Please click Exit in the tray menu once more to complete the lifecycle test."
