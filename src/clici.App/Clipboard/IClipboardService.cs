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
    string? ExceptionType);

internal sealed record ClipboardWriteResult(
    ClipboardAccessStatus Status,
    uint SequenceNumber,
    string? ExceptionType);

internal sealed record ClipboardFormatSnapshot(
    string Format,
    string Value);
