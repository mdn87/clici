namespace Clici.App;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        using var applicationContext = new TrayApplicationContext();
        Application.Run(applicationContext);
    }
}
