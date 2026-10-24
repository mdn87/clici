namespace Clici.Core.Navigation;

// Bindings come from the native renderer, never from terminal text. Painted
// anchors come from independently observed terminal attributes after rendering.
public sealed record RenderedPromptBinding(string MessageId, string Nonce);
public sealed record PaintedPromptAnchor(string Nonce, long StartRow, long EndRow);
public sealed record PaintedPromptMarker(string Nonce, long Row, bool End);
public sealed record RenderedPromptMapResult(bool Success, bool ReachesHistoryStart,
    IReadOnlyList<PromptOccurrence> Prompts);

public static class RenderedPromptMap
{
    public const int MaximumPrompts = 4096;

    public static IReadOnlyList<PaintedPromptAnchor> CompleteSuffix(
        IReadOnlyList<RenderedPromptBinding> bindings, IReadOnlyList<PaintedPromptMarker> markers, long documentRows)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        ArgumentNullException.ThrowIfNull(markers);
        if (bindings.Count is 0 or > MaximumPrompts || markers.Count == 0 || markers.Count > bindings.Count * 2 ||
            documentRows < 1 || bindings.Any(b => b is null || b.Nonce is null) ||
            markers.Any(m => m is null || m.Nonce is null || m.Row < 0 || m.Row >= documentRows)) return [];

        var known = bindings.Select(b => b.Nonce).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (markers.Any(m => !known.Contains(m.Nonce))) return [];
        var groups = markers.GroupBy(m => m.Nonce, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.OrderBy(m => m.Row).ToArray(), StringComparer.OrdinalIgnoreCase);
        var anchors = new List<PaintedPromptAnchor>();
        long previousEnd = -1;
        foreach (var binding in bindings)
        {
            if (!groups.TryGetValue(binding.Nonce, out var pair))
            {
                anchors.Clear();
                continue;
            }
            // Even discarded older evidence must agree with native order.
            // Duplicates, overlap and reversed boundaries are ambiguous, not gaps.
            if (pair.Length > 2 || pair[0].Row <= previousEnd) return [];
            previousEnd = pair[^1].Row;
            if (pair.Length == 1)
            {
                anchors.Clear();
                continue;
            }
            if (pair[0].End || !pair[1].End || pair[1].Row <= pair[0].Row + 1) return [];
            anchors.Add(new(binding.Nonce, pair[0].Row, pair[1].Row));
        }
        // Only a contiguous, complete suffix reaching the newest native turn
        // can navigate. Resolve also validates every native ID and nonce, even
        // before the gap. Its false history-start flag makes the gap a boundary.
        return Resolve(bindings, anchors, documentRows).Success ? anchors : [];
    }

    public static RenderedPromptMapResult Resolve(IReadOnlyList<RenderedPromptBinding> bindings,
        IReadOnlyList<PaintedPromptAnchor> anchors, long documentRows)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        ArgumentNullException.ThrowIfNull(anchors);
        RenderedPromptMapResult Refuse() => new(false, false, []);
        if (bindings.Count is 0 or > MaximumPrompts || anchors.Count == 0 ||
            anchors.Count > bindings.Count || documentRows < 1) return Refuse();

        var ids = new HashSet<string>(StringComparer.Ordinal);
        // Nonces are hex, so case carries no identity: agents may emit either case
        // and the painted colors are the same. Two nonces equal apart from case
        // are duplicates.
        var nonces = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var binding in bindings)
        {
            if (binding is null || string.IsNullOrWhiteSpace(binding.MessageId) || binding.MessageId.Length > 512 ||
                binding.Nonce is null || binding.Nonce.Length != 32 || !binding.Nonce.All(Uri.IsHexDigit) ||
                !ids.Add(binding.MessageId) || !nonces.Add(binding.Nonce))
                return Refuse();
        }

        if (anchors.Any(anchor => anchor is null)) return Refuse();
        var ordered = anchors.OrderBy(anchor => anchor.StartRow).ToArray();
        var first = bindings.Count - ordered.Length;
        var prompts = new List<PromptOccurrence>(ordered.Length);
        long previousEnd = -1;
        for (var index = 0; index < ordered.Length; index++)
        {
            var anchor = ordered[index];
            var binding = bindings[first + index];
            if (!string.Equals(anchor.Nonce, binding.Nonce, StringComparison.OrdinalIgnoreCase) || anchor.StartRow <= previousEnd || anchor.StartRow >= documentRows - 1 ||
                anchor.EndRow <= anchor.StartRow + 1 || anchor.EndRow >= documentRows) return Refuse();
            prompts.Add(new(anchor.StartRow + 1, anchor.EndRow, anchor.Nonce, binding.MessageId));
            previousEnd = anchor.EndRow;
        }

        return new(true, first == 0, prompts);
    }
}
