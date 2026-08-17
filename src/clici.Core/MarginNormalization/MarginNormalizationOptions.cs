namespace Clici.Core.MarginNormalization;

/// <summary>
/// Layout-confidence policy for <see cref="MarginNormalizer"/>. The former
/// two-ratio gate is replaced by a conflict-based classifier: a candidate
/// margin width is detected from the actual leading indentation, and any line
/// shallower than that margin (one-space outliers, column-zero lines) or
/// indented with a tab is treated as a conflict that blocks normalization.
/// One exception: a column-zero FIRST nonblank line is a common drag-selection
/// artifact, so it is exempt from margin measurement and left unchanged while
/// the remaining lines are dedented.
/// </summary>
public sealed record MarginNormalizationOptions(
    int MinimumNonblankLines,
    IReadOnlyList<int> CandidateMarginWidths,
    int? FixedMarginWidth)
{
    /// <summary>
    /// Automatic policy: require at least two nonblank content lines (a wrapped
    /// command plus its continuation is the smallest real case) and only accept
    /// a shared margin of exactly two or four spaces.
    /// </summary>
    public static MarginNormalizationOptions Default { get; } =
        new(2, [2, 4], null);
}
