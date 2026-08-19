using System.Reflection;

namespace Clici.App;

/// <summary>
/// Identifies which source this binary was built from.
///
/// The hardcoded <c>0.1.0</c> version cannot answer that: every build stamps
/// the identical FileVersion, so a stale install is indistinguishable from a
/// current one by inspection. The SDK records the commit in
/// <see cref="AssemblyInformationalVersionAttribute"/> and the repository
/// targets append <c>.dirty</c> for a modified working tree; this reads that
/// stamp back and surfaces it.
/// </summary>
internal static class BuildInformation
{
    /// <summary>
    /// Commit characters kept for display. Enough to be unambiguous in this
    /// repository while fitting a tray menu.
    /// </summary>
    private const int DisplayCommitLength = 12;

    /// <summary>
    /// The complete stamp, commit included — for logs, where the value is read
    /// after the fact and the full commit is worth the width.
    /// </summary>
    public static string FullVersion { get; } = ReadInformationalVersion();

    /// <summary>
    /// The stamp with the commit shortened, for the tray menu.
    /// </summary>
    public static string DisplayVersion { get; } = Shorten(FullVersion);

    /// <summary>
    /// Shortens the commit in a <c>0.1.0+&lt;commit&gt;[.dirty]</c> stamp. A
    /// value without a commit, or one shorter than the display length, is
    /// returned unchanged rather than truncated blindly.
    /// </summary>
    internal static string Shorten(string version)
    {
        var commitStart = version.IndexOf('+') + 1;
        if (commitStart == 0)
        {
            return version;
        }

        // Build metadata is dot-separated; the commit is the first identifier
        // and any suffix such as ".dirty" follows it and must be preserved.
        var commitEnd = version.IndexOf('.', commitStart);
        if (commitEnd < 0)
        {
            commitEnd = version.Length;
        }

        if (commitEnd - commitStart <= DisplayCommitLength)
        {
            return version;
        }

        return string.Concat(
            version.AsSpan(0, commitStart + DisplayCommitLength),
            version.AsSpan(commitEnd));
    }

    private static string ReadInformationalVersion()
    {
        var informational = typeof(BuildInformation).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        return string.IsNullOrWhiteSpace(informational) ? "unknown" : informational;
    }
}
