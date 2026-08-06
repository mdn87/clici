namespace Clici.Core.MarginNormalization;

public sealed record MarginNormalizationOptions(
    double MinimumMarginLineRatio,
    double MaximumColumnZeroLineRatio,
    int MarginSpaces)
{
    public static MarginNormalizationOptions Default { get; } = new(0.70, 0.20, 2);
}
