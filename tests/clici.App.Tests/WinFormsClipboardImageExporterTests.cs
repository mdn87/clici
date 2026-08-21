using System.Drawing;
using Clici.App.Clipboard;

namespace Clici.App.Tests;

public sealed class WinFormsClipboardImageExporterTests
{
    [Fact]
    public void EncodePngProducesPngBytes()
    {
        using var image = new Bitmap(2, 2);

        var bytes = WinFormsClipboardImageExporter.EncodePng(image);

        Assert.True(bytes.Length > 8);
        Assert.Equal(
            new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 },
            bytes[..8]);
    }

    [Fact]
    public void AtomicWriteOverwritesTheStableDestination()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"clici-image-export-{Guid.NewGuid():N}");
        var destination = Path.Combine(directory, "clipboard.png");

        try
        {
            WinFormsClipboardImageExporter.WritePngAtomically(
                destination,
                [1, 2, 3]);
            WinFormsClipboardImageExporter.WritePngAtomically(
                destination,
                [4, 5, 6]);

            Assert.Equal(new byte[] { 4, 5, 6 }, File.ReadAllBytes(destination));
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    [Fact]
    public void ArchivedExportKeepsTheEarlierImage()
    {
        var directory = CreateTemporaryDirectory();
        var destination = Path.Combine(directory, "clipboard.png");

        try
        {
            WinFormsClipboardImageExporter.WritePngAtomically(
                destination,
                [1, 2, 3],
                historyCount: 5,
                timestamp: new DateTimeOffset(
                    2026, 8, 21, 16, 16, 22, 500, TimeSpan.Zero));
            WinFormsClipboardImageExporter.WritePngAtomically(
                destination,
                [4, 5, 6],
                historyCount: 5,
                timestamp: new DateTimeOffset(
                    2026, 8, 21, 16, 23, 45, 250, TimeSpan.Zero));

            // The stable path is the newest image.
            Assert.Equal(new byte[] { 4, 5, 6 }, File.ReadAllBytes(destination));

            // The image the second export replaced is still readable.
            Assert.Equal(
                new byte[] { 1, 2, 3 },
                File.ReadAllBytes(
                    Path.Combine(directory, "clipboard-20260821-161622-500.png")));
            Assert.Equal(
                new byte[] { 4, 5, 6 },
                File.ReadAllBytes(
                    Path.Combine(directory, "clipboard-20260821-162345-250.png")));
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public void ZeroHistoryWritesOnlyTheStableDestination()
    {
        var directory = CreateTemporaryDirectory();
        var destination = Path.Combine(directory, "clipboard.png");

        try
        {
            WinFormsClipboardImageExporter.WritePngAtomically(
                destination,
                [1, 2, 3],
                historyCount: 0);

            Assert.Equal(
                [destination],
                Directory.GetFiles(directory));
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public void PruningKeepsTheNewestArchivesAndSparesTheStableDestination()
    {
        var directory = CreateTemporaryDirectory();
        var destination = Path.Combine(directory, "clipboard.png");

        try
        {
            var baseTime = new DateTimeOffset(
                2026, 8, 21, 16, 0, 0, 0, TimeSpan.Zero);
            for (var index = 0; index < 6; index++)
            {
                WinFormsClipboardImageExporter.WritePngAtomically(
                    destination,
                    [(byte)index],
                    historyCount: 3,
                    timestamp: baseTime.AddSeconds(index));
            }

            var archives = Directory
                .GetFiles(directory, "clipboard-*.png")
                .Select(path => Path.GetFileName(path)!)
                .Order(StringComparer.Ordinal)
                .ToArray();

            Assert.Equal(
                [
                    "clipboard-20260821-160003-000.png",
                    "clipboard-20260821-160004-000.png",
                    "clipboard-20260821-160005-000.png"
                ],
                archives);
            Assert.True(File.Exists(destination));
            Assert.Equal(new byte[] { 5 }, File.ReadAllBytes(destination));
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public void ArchiveNameCollisionDoesNotReplaceTheEarlierArchive()
    {
        var directory = CreateTemporaryDirectory();
        var destination = Path.Combine(directory, "clipboard.png");
        var timestamp = new DateTimeOffset(
            2026, 8, 21, 16, 16, 22, 500, TimeSpan.Zero);

        try
        {
            WinFormsClipboardImageExporter.WritePngAtomically(
                destination,
                [1, 2, 3],
                historyCount: 5,
                timestamp: timestamp);
            WinFormsClipboardImageExporter.WritePngAtomically(
                destination,
                [4, 5, 6],
                historyCount: 5,
                timestamp: timestamp);

            Assert.Equal(
                new byte[] { 1, 2, 3 },
                File.ReadAllBytes(
                    Path.Combine(directory, "clipboard-20260821-161622-500.png")));
            Assert.Equal(
                new byte[] { 4, 5, 6 },
                File.ReadAllBytes(
                    Path.Combine(directory, "clipboard-20260821-161622-500-2.png")));
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    private static string CreateTemporaryDirectory() =>
        Path.Combine(
            Path.GetTempPath(),
            $"clici-image-export-{Guid.NewGuid():N}");

    private static void DeleteTemporaryDirectory(string directory)
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void WslLocalhostPathIsAccepted()
    {
        const string path =
            @"\\wsl.localhost\Ubuntu\home\mdn87\agent-sandbox\drop\clipboard.png";

        var resolved = WinFormsClipboardImageExporter.ResolveDestinationPath(path);

        Assert.True(string.Equals(
            path,
            resolved,
            StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void RemoteUncPathIsRejected()
    {
        Assert.Throws<InvalidOperationException>(() =>
            WinFormsClipboardImageExporter.ResolveDestinationPath(
                @"\\server\share\clipboard.png"));
    }

    [Fact]
    public void NonPngDestinationIsRejected()
    {
        Assert.Throws<InvalidOperationException>(() =>
            WinFormsClipboardImageExporter.ResolveDestinationPath(
                Path.Combine(Path.GetTempPath(), "clipboard.jpg")));
    }
}
