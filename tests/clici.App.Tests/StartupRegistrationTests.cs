using Clici.App.Lifecycle;

namespace Clici.App.Tests;

public sealed class StartupRegistrationTests
{
    private const string ExePath = @"C:\Users\me\AppData\Local\Programs\clici\clici.exe";
    private static readonly string QuotedExePath = $"\"{ExePath}\"";

    [Fact]
    public void EnableWritesTheQuotedExecutablePathUnderTheCliciValue()
    {
        var store = new FakeStore();
        var registration = new StartupRegistration(store, ExePath);

        registration.Enable();

        Assert.Equal(QuotedExePath, store.Values["clici"]);
    }

    [Fact]
    public void DisableRemovesTheCliciValue()
    {
        var store = new FakeStore();
        store.Values["clici"] = QuotedExePath;
        var registration = new StartupRegistration(store, ExePath);

        registration.Disable();

        Assert.False(store.Values.ContainsKey("clici"));
    }

    [Fact]
    public void IsEnabledIsTrueWhenTheValueMatchesThisExecutable()
    {
        var store = new FakeStore();
        store.Values["clici"] = QuotedExePath;
        var registration = new StartupRegistration(store, ExePath);

        Assert.True(registration.IsEnabled());
    }

    [Fact]
    public void IsEnabledIsFalseWhenNoValueIsPresent()
    {
        var registration = new StartupRegistration(new FakeStore(), ExePath);

        Assert.False(registration.IsEnabled());
    }

    [Fact]
    public void IsEnabledIsFalseWhenTheValuePointsAtADifferentExecutable()
    {
        var store = new FakeStore();
        store.Values["clici"] = "\"C:\\somewhere\\else\\clici.exe\"";
        var registration = new StartupRegistration(store, ExePath);

        Assert.False(registration.IsEnabled());
    }

    [Fact]
    public void StoreExceptionsPropagateSoTheCallerCanHandleThem()
    {
        var registration = new StartupRegistration(new ThrowingStore(), ExePath);

        Assert.Throws<InvalidOperationException>(() => registration.Enable());
    }

    private sealed class FakeStore : IStartupRegistryStore
    {
        public Dictionary<string, string> Values { get; } = [];

        public string? GetValue(string name) =>
            Values.TryGetValue(name, out var value) ? value : null;

        public void SetValue(string name, string value) => Values[name] = value;

        public void DeleteValue(string name) => Values.Remove(name);
    }

    private sealed class ThrowingStore : IStartupRegistryStore
    {
        public string? GetValue(string name) => throw new InvalidOperationException();

        public void SetValue(string name, string value) => throw new InvalidOperationException();

        public void DeleteValue(string name) => throw new InvalidOperationException();
    }
}
