using Clici.Core.MarginNormalization;

namespace Clici.Core.Configuration;

public sealed record CliciConfiguration
{
    public bool Enabled { get; init; } = true;

    public string[] AllowedProcessNames { get; init; } =
    [
        "WindowsTerminal",
        "pwsh",
        "powershell",
        "cmd",
        "conhost",
        "claude",
        "codex"
    ];

    public string[] ExcludedProcessNames { get; init; } = [];

    public double MinimumMarginLineRatio { get; init; } = 0.70;

    public double MaximumColumnZeroLineRatio { get; init; } = 0.20;

    public int MarginSpacesToRemove { get; init; } = 2;

    public bool DiagnosticLogging { get; init; }

    public MarginNormalizationOptions ToNormalizationOptions() =>
        new(
            MinimumMarginLineRatio,
            MaximumColumnZeroLineRatio,
            MarginSpacesToRemove);
}
