using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Clici.App.Native;

namespace Clici.App.Clipboard;

internal sealed class WinFormsClipboardService : IClipboardService
{
    private const int MaximumAttempts = 4;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(20);

    public ClipboardReadResult TryReadText()
    {
        for (var attempt = 1; attempt <= MaximumAttempts; attempt++)
        {
            try
            {
                var sequenceBefore = NativeMethods.GetClipboardSequenceNumber();
                var dataObject = System.Windows.Forms.Clipboard.GetDataObject();

                // Require native Unicode text (autoConvert: false). A synthesized
                // conversion is not the same as the source having placed text.
                var text = dataObject is not null &&
                    dataObject.GetDataPresent(DataFormats.UnicodeText, false)
                        ? dataObject.GetData(DataFormats.UnicodeText, false) as string
                        : null;

                switch (ClassifyTextRead(
                    text,
                    NativeMethods.IsClipboardFormatAvailable(NativeMethods.CfUnicodeText),
                    attempt < MaximumAttempts))
                {
                    case TextReadOutcome.NoText:
                        return new ClipboardReadResult(
                            ClipboardAccessStatus.NoText,
                            null,
                            sequenceBefore,
                            null);
                    case TextReadOutcome.Retry:
                        Thread.Sleep(RetryDelay);
                        continue;
                    case TextReadOutcome.Unreadable:
                        return new ClipboardReadResult(
                            ClipboardAccessStatus.Busy,
                            null,
                            NativeMethods.GetClipboardSequenceNumber(),
                            "UnicodeTextUnreadable");
                }

                // Accept implies text was read from a present data object.
                if (dataObject is null || text is null)
                {
                    return new ClipboardReadResult(
                        ClipboardAccessStatus.NoText,
                        null,
                        sequenceBefore,
                        null);
                }

                var privacyPolicy = ClipboardPrivacyPolicy.FromDataObject(dataObject);
                var classification = ClipboardContentClassification.FromDataObject(dataObject);
                var owner = CaptureOwner();
                var isCliciWrite = CliciWriteMarker.IsSelfWrite(dataObject);

                var sequenceAfter = NativeMethods.GetClipboardSequenceNumber();
                if (sequenceBefore != sequenceAfter)
                {
                    if (attempt < MaximumAttempts)
                    {
                        Thread.Sleep(RetryDelay);
                        continue;
                    }

                    return new ClipboardReadResult(
                        ClipboardAccessStatus.Busy,
                        null,
                        sequenceAfter,
                        "ClipboardChangedDuringRead");
                }

                return new ClipboardReadResult(
                    ClipboardAccessStatus.Success,
                    text,
                    sequenceAfter,
                    null,
                    privacyPolicy,
                    classification.NativeFormats,
                    classification.HasNativeUnicodeText,
                    classification.HasRichText,
                    classification.HasPrimaryNonTextContent,
                    classification.HasDisallowedFormat,
                    owner.ProcessId,
                    owner.ProcessName,
                    owner.WindowClass,
                    isCliciWrite);
            }
            catch (ExternalException exception) when (attempt < MaximumAttempts)
            {
                _ = exception;
                Thread.Sleep(RetryDelay);
            }
            catch (ExternalException exception)
            {
                return new ClipboardReadResult(
                    ClipboardAccessStatus.Busy,
                    null,
                    NativeMethods.GetClipboardSequenceNumber(),
                    exception.GetType().Name);
            }
            catch (Exception exception)
            {
                return new ClipboardReadResult(
                    ClipboardAccessStatus.Failed,
                    null,
                    NativeMethods.GetClipboardSequenceNumber(),
                    exception.GetType().Name);
            }
        }

        return new ClipboardReadResult(
            ClipboardAccessStatus.Failed,
            null,
            NativeMethods.GetClipboardSequenceNumber(),
            null);
    }

    /// <summary>
    /// WinForms' OLE read swallows a contended clipboard and reports it as
    /// missing (null) or empty text, even while CF_UNICODETEXT is present. Such
    /// a read is retried; one that never yields text is reported as busy rather
    /// than as no text, so it is logged instead of silently dropped.
    /// </summary>
    internal static TextReadOutcome ClassifyTextRead(
        string? text,
        bool nativeTextAvailable,
        bool canRetry)
    {
        if (!string.IsNullOrEmpty(text))
        {
            return TextReadOutcome.Accept;
        }

        if (text is null && !nativeTextAvailable)
        {
            return TextReadOutcome.NoText;
        }

        if (canRetry)
        {
            return TextReadOutcome.Retry;
        }

        // Persistently empty text is taken as a genuinely empty item.
        return text is null ? TextReadOutcome.Unreadable : TextReadOutcome.Accept;
    }

    public ClipboardWriteResult TryWriteText(
        string text,
        ClipboardReadResult source)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(source);

        for (var attempt = 1; attempt <= MaximumAttempts; attempt++)
        {
            try
            {
                var sequenceBeforeCapture = NativeMethods.GetClipboardSequenceNumber();
                if (sequenceBeforeCapture != source.SequenceNumber)
                {
                    return new ClipboardWriteResult(
                        ClipboardAccessStatus.Stale,
                        sequenceBeforeCapture,
                        null);
                }

                var dataObject = CreateDataObject(
                    text,
                    source.PrivacyPolicy ?? ClipboardPrivacyPolicy.None);
                System.Windows.Forms.Clipboard.SetDataObject(
                    dataObject,
                    true,
                    0,
                    0);
                return new ClipboardWriteResult(
                    ClipboardAccessStatus.Success,
                    NativeMethods.GetClipboardSequenceNumber(),
                    null);
            }
            catch (ExternalException exception) when (attempt < MaximumAttempts)
            {
                _ = exception;
                Thread.Sleep(RetryDelay);
            }
            catch (ExternalException exception)
            {
                return new ClipboardWriteResult(
                    ClipboardAccessStatus.Busy,
                    NativeMethods.GetClipboardSequenceNumber(),
                    exception.GetType().Name);
            }
            catch (Exception exception)
            {
                return new ClipboardWriteResult(
                    ClipboardAccessStatus.Failed,
                    NativeMethods.GetClipboardSequenceNumber(),
                    exception.GetType().Name);
            }
        }

        return new ClipboardWriteResult(
            ClipboardAccessStatus.Failed,
            NativeMethods.GetClipboardSequenceNumber(),
            null);
    }

    internal static DataObject CreateDataObject(
        string text,
        ClipboardPrivacyPolicy privacyPolicy)
    {
        var dataObject = new DataObject();
        dataObject.SetData(DataFormats.UnicodeText, true, text);
        privacyPolicy.ApplyTo(dataObject);
        CliciWriteMarker.Apply(dataObject);
        return dataObject;
    }

    /// <summary>
    /// Captures the clipboard owner window and its process as a primary
    /// source-attribution signal. GetClipboardOwner is not perfectly reliable
    /// (clipboard brokers, ownerless states), so failures degrade to nulls and
    /// the coordinator falls back to the foreground process.
    /// </summary>
    private static ClipboardOwner CaptureOwner()
    {
        try
        {
            var ownerWindow = NativeMethods.GetClipboardOwner();
            if (ownerWindow == IntPtr.Zero)
            {
                return ClipboardOwner.Unknown;
            }

            _ = NativeMethods.GetWindowThreadProcessId(ownerWindow, out var processId);
            string? processName = null;
            if (processId != 0)
            {
                try
                {
                    using var process = Process.GetProcessById((int)processId);
                    processName = process.ProcessName;
                }
                catch (Exception exception) when (
                    exception is ArgumentException or InvalidOperationException)
                {
                    processName = null;
                }
            }

            return new ClipboardOwner(
                processId == 0 ? null : (int)processId,
                processName,
                ReadWindowClass(ownerWindow));
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return ClipboardOwner.Unknown;
        }
    }

    private static string? ReadWindowClass(IntPtr window)
    {
        var buffer = new StringBuilder(256);
        var length = NativeMethods.GetClassName(window, buffer, buffer.Capacity);
        return length > 0 ? buffer.ToString() : null;
    }

    private readonly record struct ClipboardOwner(
        int? ProcessId,
        string? ProcessName,
        string? WindowClass)
    {
        public static ClipboardOwner Unknown { get; } = new(null, null, null);
    }
}

internal enum TextReadOutcome
{
    Accept,
    Retry,
    NoText,
    Unreadable
}
