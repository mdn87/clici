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

# 1. Clean any existing
$existing = Get-Process -Name 'clici' -ErrorAction SilentlyContinue
if ($existing) {
    Stop-Process -Name 'clici' -Force
    Start-Sleep -Milliseconds 500
}

# 2. Start primary
$primary = Start-Process -FilePath $exePath -WindowStyle Hidden -PassThru
Write-Host "Primary clici started with PID $($primary.Id)"

# 3. Wait for listener
$sw = [System.Diagnostics.Stopwatch]::StartNew()
$listenerPids = @()
while ($sw.ElapsedMilliseconds -lt 5000) {
    $listenerPids = [CliciWindowProbe]::FindListenerProcessIds()
    if ($listenerPids.Count -eq 1 -and $listenerPids[0] -eq $primary.Id) {
        break
    }
    Start-Sleep -Milliseconds 100
}
if ($listenerPids.Count -ne 1 -or $listenerPids[0] -ne $primary.Id) {
    throw "Primary listener window not found for PID $($primary.Id)"
}
Write-Host "Primary listener window verified (HWND owned by PID $($primary.Id))."

# 4. Start secondary (single instance guard test)
$secondary = Start-Process -FilePath $exePath -WindowStyle Hidden -PassThru
if (-not $secondary.WaitForExit(5000)) {
    throw "Secondary process failed to exit within 5 seconds."
}
if ($secondary.ExitCode -ne 0) {
    throw "Secondary process exited with code $($secondary.ExitCode), expected 0."
}
Write-Host "Secondary process exited cleanly with code 0 (single instance guard verified)."

# 5. Check primary is still running
$procs = Get-Process -Name 'clici' -ErrorAction SilentlyContinue
if (@($procs).Count -ne 1 -or $procs[0].Id -ne $primary.Id) {
    throw "Primary process is no longer the sole clici process."
}
Write-Host "Step 1 complete. Primary PID $($primary.Id) is running with tray icon active."
