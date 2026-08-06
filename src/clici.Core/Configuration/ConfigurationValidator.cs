namespace Clici.Core.Configuration;

public static class ConfigurationValidator
{
    private static readonly CliciConfiguration Defaults = new();

    public static ConfigurationValidationResult Validate(CliciConfiguration? candidate)
    {
        if (candidate is null)
        {
            return new ConfigurationValidationResult(new CliciConfiguration(), true);
        }

        var usedFallback = false;
        var minimumMargin = ValidateRatio(
            candidate.MinimumMarginLinePercentage,
            Defaults.MinimumMarginLinePercentage,
            ref usedFallback);
        var maximumColumnZero = ValidateRatio(
            candidate.MaximumColumnZeroLinePercentage,
            Defaults.MaximumColumnZeroLinePercentage,
            ref usedFallback);
        var marginSpaces = candidate.MarginSpacesToRemove;

        if (marginSpaces is < 1 or > 16)
        {
            marginSpaces = Defaults.MarginSpacesToRemove;
            usedFallback = true;
        }

        var allowed = NormalizeProcessNames(candidate.AllowedProcessNames, ref usedFallback);
        var excluded = NormalizeProcessNames(candidate.ExcludedProcessNames, ref usedFallback);

        return new ConfigurationValidationResult(
            candidate with
            {
                AllowedProcessNames = allowed,
                ExcludedProcessNames = excluded,
                MinimumMarginLinePercentage = minimumMargin,
                MaximumColumnZeroLinePercentage = maximumColumnZero,
                MarginSpacesToRemove = marginSpaces
            },
            usedFallback);
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
        ref bool usedFallback)
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
            usedFallback = true;
        }

        return normalized;
    }
}

public sealed record ConfigurationValidationResult(
    CliciConfiguration Configuration,
    bool UsedFallback);
