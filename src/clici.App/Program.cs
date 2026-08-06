using Clici.App.Lifecycle;

namespace Clici.App;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var singleInstance = SingleInstanceGuard.TryAcquire();
        if (singleInstance is null)
        {
            return;
        }

        ApplicationConfiguration.Initialize();

        using var applicationContext = new TrayApplicationContext();
        Application.Run(applicationContext);
    }
}
