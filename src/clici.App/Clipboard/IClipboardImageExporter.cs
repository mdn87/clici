namespace Clici.App.Clipboard;

internal interface IClipboardImageExporter
{
    ClipboardImageExportResult TryExport(string destinationPath);
}

internal enum ClipboardImageExportStatus
{
    NoImage,
    Exported,
    SkippedMonitorProcessing,
    SkippedUnreadablePrivacyPolicy,
    Busy,
    Failed
}

internal sealed record ClipboardImageExportResult(
    ClipboardImageExportStatus Status,
    string? ExceptionType = null);
