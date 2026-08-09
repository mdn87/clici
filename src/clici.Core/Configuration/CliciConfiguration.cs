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

    /// <summary>
    /// When true (default), the margin width is detected automatically from the
    /// copied text and constrained to two or four spaces. When false, exactly
    /// <see cref="MarginSpacesToRemove"/> is used as a fixed profile override.
    /// </summary>
    public bool AutoDetectMarginWidth { get; init; } = true;

    public int MarginSpacesToRemove { get; init; } = 2;

    public int MaximumTextCharacters { get; init; } = 2_000_000;

    public bool DiagnosticLogging { get; init; }

    /// <summary>
    /// Configuration schema version. Introduced before source profiles and
    /// privacy policies so future readers can migrate older files.
    /// </summary>
    public int SchemaVersion { get; init; } = 1;

    public MarginNormalizationOptions ToNormalizationOptions() =>
        new(
            MinimumNonblankLines: 3,
            CandidateMarginWidths: [2, 4],
            FixedMarginWidth: AutoDetectMarginWidth ? null : MarginSpacesToRemove);
}
