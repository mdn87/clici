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
