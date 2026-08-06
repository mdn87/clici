namespace Clici.Core.Processes;

public sealed class ProcessNameMatcher
{
    public bool IsAllowed(
        string? processName,
        IEnumerable<string> allowedProcessNames,
        IEnumerable<string> excludedProcessNames)
    {
        if (string.IsNullOrWhiteSpace(processName))
        {
            return false;
        }

        var normalizedProcessName = Normalize(processName);
        var excluded = excludedProcessNames
            .Select(Normalize)
            .Contains(normalizedProcessName, StringComparer.OrdinalIgnoreCase);

        if (excluded)
        {
            return false;
        }

        return allowedProcessNames
            .Select(Normalize)
            .Contains(normalizedProcessName, StringComparer.OrdinalIgnoreCase);
    }

    private static string Normalize(string processName)
    {
        var trimmed = processName.Trim();
        return trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? trimmed[..^4]
            : trimmed;
    }
}
