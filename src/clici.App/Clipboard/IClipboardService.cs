namespace Clici.App.Clipboard;

internal interface IClipboardService
{
    ClipboardReadResult TryReadText();

    ClipboardWriteResult TryWriteText(
        string text,
        ClipboardReadResult source);
}

internal enum ClipboardAccessStatus
{
    Success,
    NoText,
    Busy,
    Stale,
    Failed
}

internal sealed record ClipboardReadResult(
    ClipboardAccessStatus Status,
    string? Text,
    uint SequenceNumber,
    string? ExceptionType,
    ClipboardPrivacyPolicy? PrivacyPolicy = null,
    IReadOnlyList<string>? NativeFormats = null,
    bool HasNativeUnicodeText = false,
    bool HasRichText = false,
    bool HasPrimaryNonTextContent = false,
    bool HasDisallowedFormat = false,
    int? OwnerProcessId = null,
    string? OwnerProcessName = null,
    string? OwnerWindowClass = null);

internal sealed record ClipboardWriteResult(
    ClipboardAccessStatus Status,
    uint SequenceNumber,
    string? ExceptionType);
