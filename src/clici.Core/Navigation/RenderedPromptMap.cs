namespace Clici.Core.Navigation;

// Bindings come from the native renderer, never from terminal text. Painted
// anchors come from independently observed terminal attributes after rendering.
public sealed record RenderedPromptBinding(string MessageId, string Nonce);
public sealed record PaintedPromptAnchor(string Nonce, long StartRow, long EndRow);
public sealed record RenderedPromptMapResult(bool Success, bool ReachesHistoryStart,
    IReadOnlyList<PromptOccurrence> Prompts);

public static class RenderedPromptMap
{
    public const int MaximumPrompts = 4096;

    public static RenderedPromptMapResult Resolve(IReadOnlyList<RenderedPromptBinding> bindings,
        IReadOnlyList<PaintedPromptAnchor> anchors, long documentRows)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        ArgumentNullException.ThrowIfNull(anchors);
        RenderedPromptMapResult Refuse() => new(false, false, []);
        if (bindings.Count is 0 or > MaximumPrompts || anchors.Count == 0 ||
            anchors.Count > bindings.Count || documentRows < 1) return Refuse();

        var ids = new HashSet<string>(StringComparer.Ordinal);
        var nonces = new HashSet<string>(StringComparer.Ordinal);
        foreach (var binding in bindings)
        {
            if (binding is null || string.IsNullOrWhiteSpace(binding.MessageId) || binding.MessageId.Length > 512 ||
                binding.Nonce is null || binding.Nonce.Length != 32 || !binding.Nonce.All(Uri.IsHexDigit) ||
                binding.Nonce != binding.Nonce.ToUpperInvariant() || !ids.Add(binding.MessageId) || !nonces.Add(binding.Nonce))
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
            if (anchor.Nonce != binding.Nonce || anchor.StartRow <= previousEnd || anchor.StartRow >= documentRows - 1 ||
                anchor.EndRow <= anchor.StartRow + 1 || anchor.EndRow >= documentRows) return Refuse();
            prompts.Add(new(anchor.StartRow + 1, anchor.EndRow, anchor.Nonce, binding.MessageId));
            previousEnd = anchor.EndRow;
        }

        return new(true, first == 0, prompts);
    }
}
