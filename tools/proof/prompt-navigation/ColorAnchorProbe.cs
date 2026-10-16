using System.Security.Cryptography;
using System.Text.Json;

namespace Clici.PromptNavigation.Probe;

// Development experiment. The six spaces occupy the renderer's existing blank
// separator row. No hidden text, control characters, or IDs enter copied text.
internal static class ColorAnchorProbe
{
    internal static int[] Colors(string nonce, bool end)
    {
        if (nonce.Length != 32 || nonce.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new ArgumentException("Expected a 128-bit hexadecimal anchor nonce.");
        }

        var bytes = Convert.FromHexString(nonce + (end ? "C3D4" : "A1B2"));
        return Enumerable.Range(0, 6).Select(index =>
            bytes[index * 3] | bytes[index * 3 + 1] << 8 | bytes[index * 3 + 2] << 16).ToArray();
    }

    internal static void Print(string nonce, bool end)
    {
        foreach (var color in Colors(nonce, end))
        {
            Console.Write($"\x1b[38;2;{color & 255};{(color >> 8) & 255};{(color >> 16) & 255}m ");
        }

        Console.Write("\x1b[0m\r\n");
    }

    internal static void Fixture(string directory)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        var anchors = Enumerable.Range(1, 3).Select(index => new
        {
            messageId = Guid.NewGuid().ToString(),
            nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)),
            index
        }).ToArray();
        File.WriteAllText(Path.Combine(directory, "anchors.json"), JsonSerializer.Serialize(anchors));
        Console.WriteLine("CLICI_FIXTURE_COLOR_ANCHORS");
        foreach (var anchor in anchors)
        {
            Print(anchor.nonce, false);
            Console.WriteLine("› CLICI_FIXTURE_DUPLICATE 日本語 🙂");
            Print(anchor.nonce, true);
            for (var line = 0; line < 32; line++)
            {
                Console.WriteLine($"CLICI_FIXTURE_REPLY_{anchor.index}_{line:00}");
            }
        }

        Console.WriteLine("› CLICI_FIXTURE_DRAFT (Q closes this fixture)");
        while (Console.ReadKey(true).Key != ConsoleKey.Q) { }
    }

}
