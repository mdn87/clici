namespace Clici.App.Input;

/// <summary>
/// Parses a hotkey chord like "Ctrl+Alt+J" into RegisterHotKey modifier flags
/// and a virtual-key code. At least one modifier is required so a plain key
/// can never be hijacked system-wide. The key may be a letter, a digit, or
/// F1–F24.
/// </summary>
internal static class HotkeyParser
{
    public static bool TryParse(string? chord, out uint modifiers, out uint virtualKey)
        => TryParseCore(chord, out modifiers, out virtualKey, allowArrows: false);

    // Parsing alone registers nothing. Navigation must use its qualified input
    // adapter; the existing global join hotkey deliberately retains its grammar.
    public static bool TryParseNavigation(string? chord, out uint modifiers, out uint virtualKey)
        => TryParseCore(chord, out modifiers, out virtualKey, allowArrows: true);

    private static bool TryParseCore(string? chord, out uint modifiers, out uint virtualKey, bool allowArrows)
    {
        modifiers = 0;
        virtualKey = 0;

        if (string.IsNullOrWhiteSpace(chord))
        {
            return false;
        }

        var tokens = chord.Split('+', StringSplitOptions.TrimEntries);
        if (tokens.Length < 2)
        {
            return false;
        }

        for (var index = 0; index < tokens.Length - 1; index++)
        {
            switch (tokens[index].ToLowerInvariant())
            {
                case "ctrl":
                case "control":
                    modifiers |= Native.NativeMethods.ModControl;
                    break;
                case "alt":
                    modifiers |= Native.NativeMethods.ModAlt;
                    break;
                case "shift":
                    modifiers |= Native.NativeMethods.ModShift;
                    break;
                case "win":
                case "windows":
                    modifiers |= Native.NativeMethods.ModWin;
                    break;
                default:
                    return false;
            }
        }

        var key = tokens[^1];
        if (allowArrows)
        {
            virtualKey = key.ToLowerInvariant() switch { "left" => 0x25, "up" => 0x26, "right" => 0x27, "down" => 0x28, _ => 0 };
            if (virtualKey != 0) { return modifiers != 0; }
        }
        if (key.Length == 1 && char.IsAsciiLetterOrDigit(key[0]))
        {
            virtualKey = char.ToUpperInvariant(key[0]);
            return modifiers != 0;
        }

        if ((key.Length is 2 or 3) &&
            (key[0] is 'F' or 'f') &&
            int.TryParse(key[1..], out var functionNumber) &&
            functionNumber is >= 1 and <= 24)
        {
            // VK_F1 is 0x70.
            virtualKey = (uint)(0x6F + functionNumber);
            return modifiers != 0;
        }

        return false;
    }
}
