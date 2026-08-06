using System.Diagnostics;
using Clici.App.Native;

namespace Clici.App.Processes;

internal sealed class WindowsForegroundProcessProvider : IForegroundProcessProvider
{
    public ForegroundProcessResult TryGetForegroundProcess()
    {
        try
        {
            var windowHandle = NativeMethods.GetForegroundWindow();
            if (windowHandle == IntPtr.Zero)
            {
                return new ForegroundProcessResult(false, null, null);
            }

            NativeMethods.GetWindowThreadProcessId(windowHandle, out var processId);
            if (processId == 0)
            {
                return new ForegroundProcessResult(false, null, null);
            }

            using var process = Process.GetProcessById(checked((int)processId));
            return new ForegroundProcessResult(true, process.ProcessName, null);
        }
        catch (Exception exception)
        {
            return new ForegroundProcessResult(
                false,
                null,
                exception.GetType().Name);
        }
    }
}
