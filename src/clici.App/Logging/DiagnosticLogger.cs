using Clici.Core.MarginNormalization;

namespace Clici.App.Logging;

internal sealed class DiagnosticLogger : IDiagnosticLogger
{
    private readonly bool _enabled;
    private readonly string _logPath;

    public DiagnosticLogger(string configurationDirectory, bool enabled)
    {
        _enabled = enabled;
        _logPath = Path.Combine(configurationDirectory, "clici.log");
    }

    public void Decision(
        string? processName,
        MarginNormalizationResult result) =>
        Write(
            $"decision status={result.Status} process={SafeProcessName(processName)} " +
            $"nonblank={result.NonblankLineCount} margin={result.MarginLineCount} " +
            $"columnZero={result.ColumnZeroLineCount} changed={result.ChangedLineCount}");

    public void Failure(
        string operation,
        string? processName,
        string? exceptionType) =>
        Write(
            $"failure operation={operation} process={SafeProcessName(processName)} " +
            $"exception={exceptionType ?? "unknown"}");

    public void Event(string eventName) =>
        Write($"event name={eventName}");

    private void Write(string message)
    {
        if (!_enabled)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_logPath)!);
            File.AppendAllText(
                _logPath,
                $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
        }
        catch
        {
            // Diagnostics are optional and may never affect clipboard handling.
        }
    }

    private static string SafeProcessName(string? processName) =>
        string.IsNullOrWhiteSpace(processName) ? "unknown" : processName;
}
