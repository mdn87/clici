namespace Clici.App.Lifecycle;

internal sealed class StartupRegistration : IStartupRegistration
{
    private const string ValueName = "clici";
    private readonly IStartupRegistryStore _store;
    private readonly string _quotedExecutablePath;

    public StartupRegistration(IStartupRegistryStore store, string executablePath)
    {
        _store = store;
        _quotedExecutablePath = $"\"{executablePath}\"";
    }

    public bool IsEnabled() =>
        string.Equals(
            _store.GetValue(ValueName),
            _quotedExecutablePath,
            StringComparison.OrdinalIgnoreCase);

    public void Enable() => _store.SetValue(ValueName, _quotedExecutablePath);

    public void Disable() => _store.DeleteValue(ValueName);
}
