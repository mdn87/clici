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
        var marginSpaces = candidate.MarginSpacesToRemove;
        var maximumTextCharacters = candidate.MaximumTextCharacters;
        var schemaVersion = candidate.SchemaVersion;

        if (marginSpaces is < 1 or > 16)
        {
            marginSpaces = Defaults.MarginSpacesToRemove;
            usedFallback = true;
        }

        if (maximumTextCharacters is < 1 or > 100_000_000)
        {
            maximumTextCharacters = Defaults.MaximumTextCharacters;
            usedFallback = true;
        }

        if (schemaVersion < 1)
        {
            schemaVersion = Defaults.SchemaVersion;
            usedFallback = true;
        }

        var joinLinesHotkey = candidate.JoinLinesHotkey;
        if (joinLinesHotkey is null)
        {
            joinLinesHotkey = Defaults.JoinLinesHotkey;
            usedFallback = true;
        }
        else if (joinLinesHotkey.Trim() != joinLinesHotkey)
        {
            joinLinesHotkey = joinLinesHotkey.Trim();
            wasNormalized = true;
        }

        var clipboardImageExportPath = candidate.ClipboardImageExportPath;
        if (clipboardImageExportPath is null)
        {
            clipboardImageExportPath = Defaults.ClipboardImageExportPath;
            usedFallback = true;
        }
        else if (clipboardImageExportPath.Trim() != clipboardImageExportPath)
        {
            clipboardImageExportPath = clipboardImageExportPath.Trim();
            wasNormalized = true;
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
                MarginSpacesToRemove = marginSpaces,
                MaximumTextCharacters = maximumTextCharacters,
                JoinLinesHotkey = joinLinesHotkey,
                ClipboardImageExportPath = clipboardImageExportPath,
                SchemaVersion = schemaVersion
            },
            usedFallback,
            wasNormalized);
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
