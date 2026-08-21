using Clici.Core.Configuration;

namespace Clici.Core.Tests;

public sealed class ClipboardImageExportConfigurationTests
{
    [Fact]
    public void ClipboardImageExportIsDisabledByDefault()
    {
        var configuration = new CliciConfiguration();

        Assert.Equal(string.Empty, configuration.ClipboardImageExportPath);
    }

    [Fact]
    public void ClipboardImageExportPathIsTrimmed()
    {
        const string path =
            @"\\wsl.localhost\Ubuntu\home\mdn87\agent-sandbox\drop\clipboard.png";
        var candidate = new CliciConfiguration
        {
            ClipboardImageExportPath = $"  {path}  "
        };

        var result = ConfigurationValidator.Validate(candidate);

        Assert.False(result.UsedFallback);
        Assert.True(result.WasNormalized);
        Assert.Equal(path, result.Configuration.ClipboardImageExportPath);
    }

    [Fact]
    public void NullClipboardImageExportPathFallsBackToDisabled()
    {
        var candidate = new CliciConfiguration
        {
            ClipboardImageExportPath = null!
        };

        var result = ConfigurationValidator.Validate(candidate);

        Assert.True(result.UsedFallback);
        Assert.Equal(string.Empty, result.Configuration.ClipboardImageExportPath);
    }
}
