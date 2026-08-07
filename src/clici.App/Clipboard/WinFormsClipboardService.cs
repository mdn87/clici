using System.Runtime.InteropServices;
using Clici.App.Native;

namespace Clici.App.Clipboard;

internal sealed class WinFormsClipboardService : IClipboardService
{
    private const int MaximumAttempts = 4;
    private const string CanIncludeInClipboardHistory = "CanIncludeInClipboardHistory";
    private const string CanUploadToCloudClipboard = "CanUploadToCloudClipboard";
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(20);
    private static readonly string[] SupplementalTextFormats =
    [
        DataFormats.Html,
        DataFormats.Rtf,
        DataFormats.CommaSeparatedValue
    ];

    public ClipboardReadResult TryReadText()
    {
        for (var attempt = 1; attempt <= MaximumAttempts; attempt++)
        {
            try
            {
                var sequenceBefore = NativeMethods.GetClipboardSequenceNumber();
                var dataObject = System.Windows.Forms.Clipboard.GetDataObject();
                if (dataObject is null ||
                    !dataObject.GetDataPresent(DataFormats.UnicodeText, true))
                {
                    return new ClipboardReadResult(
                        ClipboardAccessStatus.NoText,
                        null,
                        sequenceBefore,
                        null);
                }

                var text = dataObject.GetData(
                    DataFormats.UnicodeText,
                    true) as string;
                if (text is null)
                {
                    return new ClipboardReadResult(
                        ClipboardAccessStatus.NoText,
                        null,
                        sequenceBefore,
                        null);
                }

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
                    null);
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

                var currentDataObject =
                    System.Windows.Forms.Clipboard.GetDataObject();
                var supplementalFormats = currentDataObject is null
                    ? []
                    : CaptureSupplementalFormats(currentDataObject);
                var sequenceAfterCapture = NativeMethods.GetClipboardSequenceNumber();
                if (sequenceAfterCapture != source.SequenceNumber)
                {
                    return new ClipboardWriteResult(
                        ClipboardAccessStatus.Stale,
                        sequenceAfterCapture,
                        null);
                }

                var dataObject = CreateDataObject(
                    text,
                    supplementalFormats);
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
        IReadOnlyList<ClipboardFormatSnapshot>? supplementalFormats)
    {
        var dataObject = new DataObject();

        foreach (var snapshot in supplementalFormats ?? [])
        {
            dataObject.SetData(snapshot.Format, false, snapshot.Value);
        }

        dataObject.SetData(DataFormats.UnicodeText, true, text);
        dataObject.SetData(
            CanIncludeInClipboardHistory,
            false,
            CreateClipboardDword(1));
        dataObject.SetData(
            CanUploadToCloudClipboard,
            false,
            CreateClipboardDword(1));

        return dataObject;
    }

    private static MemoryStream CreateClipboardDword(uint value) =>
        new(BitConverter.GetBytes(value), writable: false);

    private static IReadOnlyList<ClipboardFormatSnapshot> CaptureSupplementalFormats(
        IDataObject dataObject)
    {
        var snapshots = new List<ClipboardFormatSnapshot>();

        foreach (var format in SupplementalTextFormats)
        {
            try
            {
                if (dataObject.GetDataPresent(format, false) &&
                    dataObject.GetData(format, false) is string value)
                {
                    snapshots.Add(new ClipboardFormatSnapshot(format, value));
                }
            }
            catch
            {
                // An optional format may be delayed or unavailable. Plain text
                // normalization must remain independent of supplemental formats.
            }
        }

        return snapshots;
    }
}
