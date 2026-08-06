namespace Clici.Core.Configuration;

public static class ConfigurationValidator
{
    private static readonly CliciConfiguration Defaults = new();

    public static ConfigurationValidationResult Validate(CliciConfiguration? candidate)
    {
        if (candidate is null)
        {
            return new ConfigurationValidationResult(
                new CliciConfiguration(),
                true,
                false);
        }

        var usedFallback = false;
        var wasNormalized = false;
        var minimumMargin = ValidateRatio(
            candidate.MinimumMarginLineRatio,
            Defaults.MinimumMarginLineRatio,
            ref usedFallback);
        var maximumColumnZero = ValidateRatio(
            candidate.MaximumColumnZeroLineRatio,
            Defaults.MaximumColumnZeroLineRatio,
            ref usedFallback);
        var marginSpaces = candidate.MarginSpacesToRemove;

        if (marginSpaces is < 1 or > 16)
        {
            marginSpaces = Defaults.MarginSpacesToRemove;
            usedFallback = true;
        }

        var allowed = NormalizeProcessNames(
            candidate.AllowedProcessNames,
            ref usedFallback,
            ref wasNormalized);
        var excluded = NormalizeProcessNames(
            candidate.ExcludedProcessNames,
            ref usedFallback,
            ref wasNormalized);

        return new ConfigurationValidationResult(
            candidate with
            {
                AllowedProcessNames = allowed,
                ExcludedProcessNames = excluded,
                MinimumMarginLineRatio = minimumMargin,
                MaximumColumnZeroLineRatio = maximumColumnZero,
                MarginSpacesToRemove = marginSpaces
            },
            usedFallback,
            wasNormalized);
    }

    private static double ValidateRatio(double candidate, double fallback, ref bool usedFallback)
    {
        if (double.IsFinite(candidate) && candidate is >= 0 and <= 1)
        {
            return candidate;
        }

        usedFallback = true;
        return fallback;
    }

    private static string[] NormalizeProcessNames(
        string[]? processNames,
        ref bool usedFallback,
        ref bool wasNormalized)
    {
        if (processNames is null)
        {
            usedFallback = true;
            return [];
        }

        var normalized = processNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (normalized.Length != processNames.Length)
        {
            wasNormalized = true;
        }
        else if (!normalized.SequenceEqual(processNames, StringComparer.Ordinal))
        {
            wasNormalized = true;
        }

        return normalized;
    }
}

public sealed record ConfigurationValidationResult(
    CliciConfiguration Configuration,
    bool UsedFallback,
    bool WasNormalized);
