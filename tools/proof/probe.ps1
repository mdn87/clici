$procs = Get-Process -Name 'clici' -ErrorAction SilentlyContinue
if (-not $procs) {
    Write-Host "No clici process running."
    exit 0
}

Add-Type -TypeDefinition @'
using System;
using System.Text;
using System.Runtime.InteropServices;
public class Probe {
    public delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc lpEnumFunc, IntPtr lParam);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    public static void ListAll(uint targetPid) {
        EnumWindows((hWnd, _) => {
            GetWindowThreadProcessId(hWnd, out var pid);
            if (pid == targetPid) {
                var sb = new StringBuilder(256);
                GetWindowText(hWnd, sb, 256);
                Console.WriteLine($"HWND: 0x{hWnd.ToInt64():X} Title: '{sb}'");
            }
            return true;
        }, IntPtr.Zero);
    }
}
'@

foreach ($p in $procs) {
    Write-Host "Process ID: $($p.Id)"
    [Probe]::ListAll($p.Id)
}
