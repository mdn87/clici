using Clici.Core.Configuration;
using Clici.Core.Processes;

namespace Clici.Core.Tests;

public sealed class ConfigurationAndProcessTests
{
    [Fact]
    public void DefaultsContainExpectedTerminalCandidates()
    {
        var configuration = new CliciConfiguration();

        Assert.True(configuration.Enabled);
        Assert.Contains("WindowsTerminal", configuration.AllowedProcessNames);
        Assert.Contains("pwsh", configuration.AllowedProcessNames);
        Assert.Contains("codex", configuration.AllowedProcessNames);
        Assert.Empty(configuration.ExcludedProcessNames);
        Assert.Equal(2_000_000, configuration.MaximumTextCharacters);
        Assert.True(configuration.AutoDetectMarginWidth);
        Assert.Equal(1, configuration.SchemaVersion);
        Assert.False(configuration.DiagnosticLogging);
    }

    [Fact]
    public void InvalidConfigurationValuesFallBackSafely()
    {
        var candidate = new CliciConfiguration
        {
            MarginSpacesToRemove = 0,
            MaximumTextCharacters = -1,
            SchemaVersion = 0,
            AllowedProcessNames = [" pwsh ", "PWSH", ""],
            ExcludedProcessNames = null!
        };

        var result = ConfigurationValidator.Validate(candidate);

        Assert.True(result.UsedFallback);
        Assert.True(result.WasNormalized);
        Assert.Equal(2, result.Configuration.MarginSpacesToRemove);
        Assert.Equal(2_000_000, result.Configuration.MaximumTextCharacters);
        Assert.Equal(1, result.Configuration.SchemaVersion);
        Assert.Equal(["pwsh"], result.Configuration.AllowedProcessNames);
        Assert.Empty(result.Configuration.ExcludedProcessNames);
    }

    [Fact]
    public void ProcessNameCleanupIsNormalizationRatherThanFallback()
    {
        var candidate = new CliciConfiguration
        {
            AllowedProcessNames = [" pwsh ", "PWSH", ""]
        };

        var result = ConfigurationValidator.Validate(candidate);

        Assert.False(result.UsedFallback);
        Assert.True(result.WasNormalized);
        Assert.Equal(["pwsh"], result.Configuration.AllowedProcessNames);
    }

    [Fact]
    public void ProcessMatchingIsCaseInsensitiveAndAcceptsExeSuffix()
    {
        var matcher = new ProcessNameMatcher();

        var matched = matcher.IsAllowed(
            "PWSH",
            ["pwsh.exe"],
            []);

        Assert.True(matched);
    }

    [Fact]
    public void ExcludedProcessNamesTakePrecedence()
    {
        var matcher = new ProcessNameMatcher();

        var matched = matcher.IsAllowed(
            "pwsh",
            ["pwsh"],
            ["PWSH.exe"]);

        Assert.False(matched);
    }
}
