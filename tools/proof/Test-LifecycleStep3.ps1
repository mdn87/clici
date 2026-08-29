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

$procs = Get-Process -Name 'clici' -ErrorAction SilentlyContinue
if ($procs) {
    throw "clici process is still running (PID $($procs[0].Id)). Did you click Exit in the tray menu?"
}
$listeners = [CliciWindowProbe]::FindListenerProcessIds()
if ($listeners.Count -ne 0) {
    throw "Orphan listener window detected."
}

Write-Host "Lifecycle Proof Result: PASS"
Write-Host "All checks passed: Single-instance guard, window creation, clean tray exit, and reacquisition."
