using System.Runtime.InteropServices;
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
                var sequenceNumber = NativeMethods.GetClipboardSequenceNumber();
                if (!System.Windows.Forms.Clipboard.ContainsText())
                {
                    return new ClipboardReadResult(
                        ClipboardAccessStatus.NoText,
                        null,
                        sequenceNumber,
                        null);
                }

                return new ClipboardReadResult(
                    ClipboardAccessStatus.Success,
                    System.Windows.Forms.Clipboard.GetText(TextDataFormat.UnicodeText),
                    sequenceNumber,
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

    public ClipboardWriteResult TryWriteText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        for (var attempt = 1; attempt <= MaximumAttempts; attempt++)
        {
            try
            {
                System.Windows.Forms.Clipboard.SetText(text, TextDataFormat.UnicodeText);
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
}
