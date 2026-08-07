namespace Clici.App.Lifecycle;

/// <summary>
/// Controls whether clici launches at user sign-in, via the per-user Run key.
/// </summary>
internal interface IStartupRegistration
{
    bool IsEnabled();

    void Enable();

    void Disable();
}
