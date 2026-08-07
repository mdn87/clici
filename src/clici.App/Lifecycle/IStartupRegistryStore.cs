namespace Clici.App.Lifecycle;

/// <summary>
/// Minimal seam over the per-user Run registry key so startup logic is testable
/// without touching the real HKCU hive.
/// </summary>
internal interface IStartupRegistryStore
{
    string? GetValue(string name);

    void SetValue(string name, string value);

    void DeleteValue(string name);
}
