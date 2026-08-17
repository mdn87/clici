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
