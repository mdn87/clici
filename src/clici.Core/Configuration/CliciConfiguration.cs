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

    /// <summary>
    /// When true (default), a trusted copy carrying the wrap signature — no
    /// blank lines, every line except the last filled to a near-uniform right
    /// edge — is rejoined into the single logical line the terminal wrapped.
    /// </summary>
    public bool JoinWrappedLines { get; init; } = true;

    /// <summary>
    /// Global hotkey that joins the current clipboard lines unconditionally,
    /// for wrapped commands the automatic signature refuses. Empty disables
    /// the hotkey. At least one modifier is required.
    /// </summary>
    public string JoinLinesHotkey { get; init; } = "Ctrl+Alt+J";

    /// <summary>
    /// Optional fully qualified Windows destination for clipboard images. A
    /// local path or a WSL path under \\wsl.localhost is accepted. Images are
    /// written as PNG without replacing or otherwise changing the clipboard.
    /// Empty disables image export.
    /// </summary>
    public string ClipboardImageExportPath { get; init; } = string.Empty;

    /// <summary>
    /// How many timestamped copies of exported clipboard images to keep beside
    /// the stable destination. Each export writes
    /// <c>clipboard-yyyyMMdd-HHmmss-fff.png</c> as well as the stable file, so a
    /// second image arriving before the first is read does not destroy it. The
    /// oldest archives beyond this count are deleted. Zero keeps only the
    /// stable destination and restores the plain overwrite behavior.
    /// </summary>
    public int ClipboardImageExportHistory { get; init; } = 20;

    public bool DiagnosticLogging { get; init; }

    /// <summary>
    /// Configuration schema version. Introduced before source profiles and
    /// privacy policies so future readers can migrate older files.
    /// </summary>
    public int SchemaVersion { get; init; } = 1;

    public MarginNormalizationOptions ToNormalizationOptions() =>
        new(
            MinimumNonblankLines: 2,
            CandidateMarginWidths: [2, 4],
            FixedMarginWidth: AutoDetectMarginWidth ? null : MarginSpacesToRemove);
}
