namespace Clici.Core.MarginNormalization;

public enum MarginNormalizationStatus
{
    NotEligible,
    EligibleUnchanged,
    Normalized,
    FailedSafely
}

public sealed record MarginNormalizationResult(
    MarginNormalizationStatus Status,
    string Text,
    int NonblankLineCount,
    int MarginLineCount,
    int ColumnZeroLineCount,
    int ChangedLineCount,
    string? ExceptionType)
{
    public static MarginNormalizationResult NotEligible(
        string text,
        int nonblankLineCount = 0,
        int marginLineCount = 0,
        int columnZeroLineCount = 0) =>
        new(
            MarginNormalizationStatus.NotEligible,
            text,
            nonblankLineCount,
            marginLineCount,
            columnZeroLineCount,
            0,
            null);

    public static MarginNormalizationResult EligibleUnchanged(
        string text,
        int nonblankLineCount,
        int marginLineCount,
        int columnZeroLineCount) =>
        new(
            MarginNormalizationStatus.EligibleUnchanged,
            text,
            nonblankLineCount,
            marginLineCount,
            columnZeroLineCount,
            0,
            null);

    public static MarginNormalizationResult Normalized(
        string text,
        int nonblankLineCount,
        int marginLineCount,
        int columnZeroLineCount,
        int changedLineCount) =>
        new(
            MarginNormalizationStatus.Normalized,
            text,
            nonblankLineCount,
            marginLineCount,
            columnZeroLineCount,
            changedLineCount,
            null);

    public static MarginNormalizationResult FailedSafely(
        string originalText,
        string exceptionType) =>
        new(
            MarginNormalizationStatus.FailedSafely,
            originalText,
            0,
            0,
            0,
            0,
            exceptionType);
}
