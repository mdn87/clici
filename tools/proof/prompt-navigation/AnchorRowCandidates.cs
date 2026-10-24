namespace Clici.PromptNavigation.Probe;

internal static class AnchorRowCandidates
{
    // GetText omits soft-wrap boundaries. Textual row offsets are usable only
    // when their count equals the provider's physical row count. Otherwise the
    // caller subdivides its UIA range and checks again; it never guesses wraps.
    internal static (bool Aligned, List<int> Rows) Analyze(string text, int physicalRows)
    {
        var rows = new List<int>();
        var count = 0;
        foreach (var line in text.AsSpan().EnumerateLines())
        {
            if (line.Length >= 6 && line.IndexOfAnyExcept(' ') < 0) rows.Add(count);
            count++;
        }
        // EnumerateLines includes an empty item after a terminal newline.
        if (text.EndsWith('\n') || text.EndsWith('\r')) count--;
        return (count == physicalRows, rows);
    }
}
