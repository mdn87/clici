using System.Globalization;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Clici.App.Native;

namespace Clici.App.Clipboard;

/// <summary>
/// Reads an image from the Windows clipboard and writes a detached PNG copy to
/// an explicitly configured local or WSL path. It never replaces clipboard
/// content and does not register or intercept the Windows snipping hotkey.
/// </summary>
internal sealed class WinFormsClipboardImageExporter : IClipboardImageExporter
{
    private const int MaximumAttempts = 4;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(20);

    public ClipboardImageExportResult TryExport(string destinationPath, int historyCount)
    {
        for (var attempt = 1; attempt <= MaximumAttempts; attempt++)
        {
            var capture = CaptureCurrentImage();

            if ((capture.Status is ImageCaptureStatus.Busy or ImageCaptureStatus.Stale) &&
                attempt < MaximumAttempts)
            {
                Thread.Sleep(RetryDelay);
                continue;
            }

            switch (capture.Status)
            {
                case ImageCaptureStatus.NoImage:
                    return new ClipboardImageExportResult(
                        ClipboardImageExportStatus.NoImage);
                case ImageCaptureStatus.SkippedMonitorProcessing:
                    return new ClipboardImageExportResult(
                        ClipboardImageExportStatus.SkippedMonitorProcessing);
                case ImageCaptureStatus.SkippedUnreadablePrivacyPolicy:
                    return new ClipboardImageExportResult(
                        ClipboardImageExportStatus.SkippedUnreadablePrivacyPolicy);
                case ImageCaptureStatus.Busy:
                case ImageCaptureStatus.Stale:
                    return new ClipboardImageExportResult(
                        ClipboardImageExportStatus.Busy,
                        capture.ExceptionType);
                case ImageCaptureStatus.Failed:
                    return new ClipboardImageExportResult(
                        ClipboardImageExportStatus.Failed,
                        capture.ExceptionType);
                case ImageCaptureStatus.Captured:
                    break;
                default:
                    return new ClipboardImageExportResult(
                        ClipboardImageExportStatus.Failed,
                        "UnknownImageCaptureStatus");
            }

            try
            {
                using var image = capture.Image!;
                var pngBytes = EncodePng(image);
                WritePngAtomically(
                    destinationPath,
                    pngBytes,
                    historyCount,
                    DateTimeOffset.Now);
                return new ClipboardImageExportResult(
                    ClipboardImageExportStatus.Exported);
            }
            catch (Exception exception)
            {
                return new ClipboardImageExportResult(
                    ClipboardImageExportStatus.Failed,
                    exception.GetType().Name);
            }
        }

        return new ClipboardImageExportResult(
            ClipboardImageExportStatus.Failed,
            "ImageCaptureAttemptsExhausted");
    }

    internal static byte[] EncodePng(Image image)
    {
        ArgumentNullException.ThrowIfNull(image);

        using var stream = new MemoryStream();
        image.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }

    internal static void WritePngAtomically(
        string destinationPath,
        byte[] pngBytes,
        int historyCount = 0,
        DateTimeOffset? timestamp = null)
    {
        ArgumentNullException.ThrowIfNull(pngBytes);

        var resolvedPath = ResolveDestinationPath(destinationPath);
        var directoryPath = Path.GetDirectoryName(resolvedPath);
        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            throw new InvalidOperationException(
                "Clipboard image export requires a destination directory.");
        }

        Directory.CreateDirectory(directoryPath);

        // The timestamped archive is written first. If the stable destination
        // is locked or otherwise unwritable the image still survives on disk,
        // and a reader watching the stable path never sees a newer image that
        // has no archived copy behind it.
        if (historyCount > 0)
        {
            WriteThroughTemporaryFile(
                directoryPath,
                BuildArchivePath(resolvedPath, timestamp ?? DateTimeOffset.Now),
                pngBytes,
                overwrite: false);
            PruneArchives(resolvedPath, historyCount);
        }

        WriteThroughTemporaryFile(
            directoryPath,
            resolvedPath,
            pngBytes,
            overwrite: true);
    }

    /// <summary>
    /// Names the archived copy for an export. Timestamped names sort
    /// chronologically as plain text, which is what lets pruning and
    /// "newest first" listings work without reading file metadata.
    /// </summary>
    internal static string BuildArchivePath(
        string resolvedPath,
        DateTimeOffset timestamp)
    {
        var directoryPath = Path.GetDirectoryName(resolvedPath)!;
        var stem = Path.GetFileNameWithoutExtension(resolvedPath);
        var extension = Path.GetExtension(resolvedPath);
        var stamp = timestamp.ToString(
            "yyyyMMdd-HHmmss-fff",
            CultureInfo.InvariantCulture);

        var candidate = Path.Combine(
            directoryPath,
            $"{stem}-{stamp}{extension}");

        // Two images inside one millisecond are not expected, but a collision
        // must not silently replace the archive it was meant to protect.
        for (var suffix = 2; suffix <= 99 && File.Exists(candidate); suffix++)
        {
            candidate = Path.Combine(
                directoryPath,
                $"{stem}-{stamp}-{suffix}{extension}");
        }

        return candidate;
    }

    /// <summary>
    /// Deletes the oldest archives beyond <paramref name="historyCount"/>.
    /// Called after the newest archive exists, so the count is exact.
    /// </summary>
    internal static void PruneArchives(string resolvedPath, int historyCount)
    {
        if (historyCount <= 0)
        {
            return;
        }

        var directoryPath = Path.GetDirectoryName(resolvedPath)!;
        var stem = Path.GetFileNameWithoutExtension(resolvedPath);
        var extension = Path.GetExtension(resolvedPath);
        var stableName = Path.GetFileName(resolvedPath);

        string[] archives;
        try
        {
            archives = Directory.GetFiles(
                directoryPath,
                $"{stem}-*{extension}");
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return;
        }

        Array.Sort(archives, StringComparer.OrdinalIgnoreCase);

        var excess = archives.Length - historyCount;
        for (var index = 0; index < excess; index++)
        {
            // Windows short-name matching can make a search pattern reach
            // further than it reads; the stable destination is never an
            // archive and must never be pruned.
            if (string.Equals(
                    Path.GetFileName(archives[index]),
                    stableName,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                File.Delete(archives[index]);
            }
            catch
            {
                // Pruning is best effort. A locked archive is retried on the
                // next export rather than failing the export itself.
            }
        }
    }

    private static void WriteThroughTemporaryFile(
        string directoryPath,
        string targetPath,
        byte[] pngBytes,
        bool overwrite)
    {
        var temporaryPath = Path.Combine(
            directoryPath,
            $".{Path.GetFileName(targetPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            File.WriteAllBytes(temporaryPath, pngBytes);
            File.Move(temporaryPath, targetPath, overwrite);
        }
        finally
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch
            {
                // A failed cleanup must not affect later clipboard handling.
            }
        }
    }

    internal static string ResolveDestinationPath(string destinationPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        var expandedPath = Environment.ExpandEnvironmentVariables(
            destinationPath.Trim());

        if (!Path.IsPathFullyQualified(expandedPath))
        {
            throw new InvalidOperationException(
                "Clipboard image export requires a fully qualified path.");
        }

        if (expandedPath.StartsWith(@"\\", StringComparison.Ordinal) &&
            !expandedPath.StartsWith(
                @"\\wsl.localhost\",
                StringComparison.OrdinalIgnoreCase) &&
            !expandedPath.StartsWith(
                @"\\wsl$\",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Remote UNC clipboard image export paths are not allowed.");
        }

        var resolvedPath = Path.GetFullPath(expandedPath);
        if (!string.Equals(
                Path.GetExtension(resolvedPath),
                ".png",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Clipboard image export destination must end in .png.");
        }

        return resolvedPath;
    }

    private static ImageCaptureResult CaptureCurrentImage()
    {
        try
        {
            var sequenceBefore = NativeMethods.GetClipboardSequenceNumber();
            var dataObject = System.Windows.Forms.Clipboard.GetDataObject();

            if (dataObject is null ||
                !dataObject.GetDataPresent(DataFormats.Bitmap, true))
            {
                return new ImageCaptureResult(ImageCaptureStatus.NoImage);
            }

            var privacyPolicy = ClipboardPrivacyPolicy.FromDataObject(dataObject);
            if (privacyPolicy.ExcludeFromMonitorProcessing)
            {
                return new ImageCaptureResult(
                    ImageCaptureStatus.SkippedMonitorProcessing);
            }

            if (privacyPolicy.ReadFailed)
            {
                return new ImageCaptureResult(
                    ImageCaptureStatus.SkippedUnreadablePrivacyPolicy);
            }

            if (dataObject.GetData(DataFormats.Bitmap, true) is not Image sourceImage)
            {
                return new ImageCaptureResult(ImageCaptureStatus.NoImage);
            }

            Bitmap capturedImage;
            using (sourceImage)
            {
                capturedImage = new Bitmap(sourceImage);
            }

            var sequenceAfter = NativeMethods.GetClipboardSequenceNumber();
            if (sequenceBefore != sequenceAfter)
            {
                capturedImage.Dispose();
                return new ImageCaptureResult(
                    ImageCaptureStatus.Stale,
                    ExceptionType: "ClipboardChangedDuringImageRead");
            }

            return new ImageCaptureResult(
                ImageCaptureStatus.Captured,
                capturedImage);
        }
        catch (ExternalException exception)
        {
            return new ImageCaptureResult(
                ImageCaptureStatus.Busy,
                ExceptionType: exception.GetType().Name);
        }
        catch (Exception exception)
        {
            return new ImageCaptureResult(
                ImageCaptureStatus.Failed,
                ExceptionType: exception.GetType().Name);
        }
    }

    private enum ImageCaptureStatus
    {
        Captured,
        NoImage,
        SkippedMonitorProcessing,
        SkippedUnreadablePrivacyPolicy,
        Busy,
        Stale,
        Failed
    }

    private sealed record ImageCaptureResult(
        ImageCaptureStatus Status,
        Bitmap? Image = null,
        string? ExceptionType = null);
}
