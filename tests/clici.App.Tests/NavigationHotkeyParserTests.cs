using Clici.App.Input;

namespace Clici.App.Tests;

public sealed class NavigationHotkeyParserTests
{
    [Theory]
    [InlineData("Ctrl+Up", 2u, 0x26u)]
    [InlineData("control + DOWN", 2u, 0x28u)]
    [InlineData("Alt+Shift+Left", 5u, 0x25u)]
    [InlineData("Win+Right", 8u, 0x27u)]
    [InlineData("Ctrl+F12", 2u, 0x7Bu)]
    public void NavigationAcceptsModifiedArrowsAndExistingKeyForms(string chord, uint modifiers, uint key)
    {
        Assert.True(HotkeyParser.TryParseNavigation(chord, out var actualModifiers, out var actualKey));
        Assert.Equal(modifiers, actualModifiers);
        Assert.Equal(key, actualKey);
    }

    [Theory]
    [InlineData("Up")]
    [InlineData("Ctrl++Up")]
    [InlineData("Ctrl+ArrowUp")]
    [InlineData("Ctrl+")]
    public void InvalidOrUnmodifiedNavigationChordIsRefused(string chord)
    {
        Assert.False(HotkeyParser.TryParseNavigation(chord, out _, out _));
    }

    [Fact]
    public void ExistingGlobalJoinHotkeyDoesNotGainArrowBindings()
    {
        Assert.False(HotkeyParser.TryParse("Ctrl+Up", out _, out _));
        Assert.True(HotkeyParser.TryParse("Ctrl+Alt+J", out var modifiers, out var key));
        Assert.Equal(3u, modifiers);
        Assert.Equal(0x4Au, key);
    }
}
