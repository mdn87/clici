using Clici.App.Clipboard;

namespace Clici.App.Tests;

public sealed class WinFormsClipboardServiceTests
{
    [Fact]
    public void ReplacementDataPreservesRichTextAndRequestsHistoryAndCloud()
    {
        var dataObject = WinFormsClipboardService.CreateDataObject(
            "normalized",
            [
                new ClipboardFormatSnapshot(DataFormats.Html, "<pre>original</pre>"),
                new ClipboardFormatSnapshot(DataFormats.Rtf, @"{\rtf1 original}")
            ]);

        Assert.True(dataObject.TryGetData<string>(
            DataFormats.UnicodeText,
            true,
            out var text));
        Assert.Equal("normalized", text);
        Assert.True(dataObject.TryGetData<string>(
            DataFormats.Html,
            false,
            out var html));
        Assert.Equal("<pre>original</pre>", html);
        Assert.True(dataObject.TryGetData<string>(
            DataFormats.Rtf,
            false,
            out var rtf));
        Assert.Equal(@"{\rtf1 original}", rtf);
        AssertDword(dataObject, "CanIncludeInClipboardHistory", 1);
        AssertDword(dataObject, "CanUploadToCloudClipboard", 1);
    }

    private static void AssertDword(
        DataObject dataObject,
        string format,
        uint expected)
    {
        Assert.True(dataObject.TryGetData<MemoryStream>(
            format,
            false,
            out var stream));
        Assert.NotNull(stream);
        Assert.Equal(expected, BitConverter.ToUInt32(stream.ToArray()));
    }
}
