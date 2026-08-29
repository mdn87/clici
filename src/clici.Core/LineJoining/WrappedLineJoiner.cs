namespace Clici.Core.LineJoining;

public enum LineJoinStatus
{
    NotEligible,
    Joined
}

public sealed record LineJoinResult(
    LineJoinStatus Status,
    string Text,
    int SourceLineCount)
{
    public static LineJoinResult NotEligible(string text, int sourceLineCount = 0) =>
        new(LineJoinStatus.NotEligible, text, sourceLineCount);

    public static LineJoinResult Joined(string text, int sourceLineCount) =>
        new(LineJoinStatus.Joined, text, sourceLineCount);
}

/// <summary>
/// Rejoins a single logical line that a terminal wrapped at its right edge.
/// The automatic path accepts only the wrap signature: no blank lines, every
/// line except the last running long at a near-uniform width, and no
/// table/box-drawing framing. It further requires evidence that the seams are
/// word boundaries, since a space joined across a token the terminal split by
/// column would corrupt it. Genuinely multiline content — code, lists,
/// paragraphs separated by blank lines, tables — does not match and is left
/// for margin normalization. The unconditional path backs the explicit
/// user-invoked hotkey, where intent substitutes for the signature.
/// </summary>
public sealed class WrappedLineJoiner
{
    /// <summary>
    /// A line must run at least this long before it can be read as "filled to
    /// the terminal edge". Short lines are ordinary content, not wraps.
    /// </summary>
    public const int MinimumWrapColumn = 60;

    /// <summary>
    /// Word-aware wrapping leaves a ragged right edge; non-final lines may be
    /// up to this many characters shorter than the longest line and still
    /// carry the wrap signature.
    /// </summary>
    public const int RaggedEdgeTolerance = 15;

    public LineJoinResult JoinIfWrapSignature(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return LineJoinResult.NotEligible(text ?? string.Empty);
        }

        var segments = SplitLines(text);

        // A trailing terminator produces trailing empty segments; those are the
        // copy's tail, not content structure, and are dropped before analysis.
        var lineCount = segments.Count;
        while (lineCount > 0 && IsBlank(segments[lineCount - 1]))
        {
            lineCount--;
        }

        if (lineCount < 2)
        {
            return LineJoinResult.NotEligible(text, lineCount);
        }

        // A blank line between content lines is a paragraph or block break —
        // never the middle of one wrapped logical line.
        for (var index = 0; index < lineCount; index++)
        {
            if (IsBlank(segments[index]))
            {
                return LineJoinResult.NotEligible(text, lineCount);
            }

            if (StartsWithStructuralCharacter(segments[index]))
            {
                return LineJoinResult.NotEligible(text, lineCount);
            }
        }

        // Wrap signature: every line except the last is filled to a
        // near-uniform right edge, and the last line fits inside that width.
        var maximumNonFinalLength = 0;
        var minimumNonFinalLength = int.MaxValue;
        for (var index = 0; index < lineCount - 1; index++)
        {
            var length = segments[index].TrimEnd().Length;
            maximumNonFinalLength = Math.Max(maximumNonFinalLength, length);
            minimumNonFinalLength = Math.Min(minimumNonFinalLength, length);
        }

        if (minimumNonFinalLength < MinimumWrapColumn ||
            minimumNonFinalLength < maximumNonFinalLength - RaggedEdgeTolerance)
        {
            return LineJoinResult.NotEligible(text, lineCount);
        }

        var finalLength = segments[lineCount - 1].TrimEnd().Length;
        if (finalLength > maximumNonFinalLength + RaggedEdgeTolerance)
        {
            return LineJoinResult.NotEligible(text, lineCount);
        }

        // A seam is only safe to close with a space when the terminal broke at
        // a word boundary and dropped that space. A terminal that fills the row
        // and continues the same token on the next line drops nothing, so a
        // space inserted there corrupts the token — a wrapped URL, path, hash,
        // or base64 blob is the everyday case. Refuse both signatures of that.
        if (!ContainsInternalWhitespace(segments, lineCount))
        {
            return LineJoinResult.NotEligible(text, lineCount);
        }

        if (HasFlushRightEdge(lineCount, minimumNonFinalLength, maximumNonFinalLength))
        {
            return LineJoinResult.NotEligible(text, lineCount);
        }

        return LineJoinResult.Joined(JoinSegments(segments, lineCount), lineCount);
    }

    /// <summary>
    /// Joins every nonblank line with a single space, unconditionally. Backs
    /// the explicit hotkey, where the user has asserted the copy is one
    /// logical line.
    /// </summary>
    public LineJoinResult JoinAllLines(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return LineJoinResult.NotEligible(text ?? string.Empty);
        }

        var segments = SplitLines(text);
        var nonblank = segments.Where(segment => !IsBlank(segment)).ToList();
        if (nonblank.Count < 2)
        {
            return LineJoinResult.NotEligible(text, nonblank.Count);
        }

        return LineJoinResult.Joined(
            string.Join(' ', nonblank.Select(segment => segment.Trim())),
            nonblank.Count);
    }

    private static string JoinSegments(IReadOnlyList<string> segments, int lineCount)
    {
        var trimmed = new string[lineCount];
        for (var index = 0; index < lineCount; index++)
        {
            trimmed[index] = segments[index].Trim();
        }

        return string.Join(' ', trimmed);
    }

    private static List<string> SplitLines(string text)
    {
        var lines = new List<string>();
        var start = 0;
        var index = 0;

        while (index < text.Length)
        {
            if (text[index] is not ('\r' or '\n'))
            {
                index++;
                continue;
            }

            lines.Add(text[start..index]);
            index += text[index] == '\r' &&
                     index + 1 < text.Length &&
                     text[index + 1] == '\n'
                ? 2
                : 1;
            start = index;
        }

        lines.Add(text[start..]);
        return lines;
    }

    private static bool IsBlank(string segment) =>
        string.IsNullOrWhiteSpace(segment);

    /// <summary>
    /// Reports whether any content line carries whitespace between its first
    /// and last nonspace characters. Content with none is a single unbroken
    /// token that the terminal split by column, so no seam in it can be the
    /// word boundary a space join assumes.
    /// </summary>
    private static bool ContainsInternalWhitespace(
        IReadOnlyList<string> segments,
        int lineCount)
    {
        for (var index = 0; index < lineCount; index++)
        {
            var trimmed = segments[index].Trim();
            for (var position = 0; position < trimmed.Length; position++)
            {
                if (char.IsWhiteSpace(trimmed[position]))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Reports whether the non-final lines are all exactly the same width.
    /// Word wrapping pushes a whole word down when it does not fit and so
    /// leaves a ragged edge; an edge flush to a single column is what wrapping
    /// mid-token produces, and its seams cannot be assumed to be word
    /// boundaries. Uniformity is only evidence when at least two non-final
    /// lines were measured — the single non-final line of a two-line copy is
    /// trivially uniform and carries no width evidence either way.
    /// </summary>
    private static bool HasFlushRightEdge(
        int lineCount,
        int minimumNonFinalLength,
        int maximumNonFinalLength) =>
        lineCount - 1 >= 2 && minimumNonFinalLength == maximumNonFinalLength;

    private static bool StartsWithStructuralCharacter(string segment)
    {
        var trimmed = segment.TrimStart();
        if (trimmed.Length == 0)
        {
            return false;
        }

        var first = trimmed[0];

        // Table and framing prefixes: markdown/ASCII table pipes and borders,
        // and the Unicode box-drawing block. Rows of framed output are uniform
        // full-width lines and would otherwise satisfy the wrap signature.
        return first is '|' or '+' || (first >= '─' && first <= '╿');
    }
}
