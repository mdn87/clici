using Clici.App.Clipboard;

namespace Clici.App.Tests;

public sealed class CliciWriteMarkerTests
{
    [Fact]
    public void SelfWrittenItemsCarryTheMarker()
    {
        var dataObject = WinFormsClipboardService.CreateDataObject(
            "normalized",
            ClipboardPrivacyPolicy.None);

        Assert.True(CliciWriteMarker.IsSelfWrite(dataObject));
    }

    [Fact]
    public void ForeignItemsAreNotDetectedAsSelfWrites()
    {
        var dataObject = new DataObject();
        dataObject.SetData(DataFormats.UnicodeText, true, "normalized");

        Assert.False(CliciWriteMarker.IsSelfWrite(dataObject));
    }

    [Fact]
    public void ItemsCarryingAForeignTokenAreNotDetectedAsSelfWrites()
    {
        // A broker could forward a different token; only this process's exact
        // marker counts as a self-write.
        var dataObject = new DataObject();
        dataObject.SetData(DataFormats.UnicodeText, true, "normalized");
        dataObject.SetData(
            CliciWriteMarker.FormatName,
            false,
            new MemoryStream([9, 9, 9, 9], writable: false));

        Assert.False(CliciWriteMarker.IsSelfWrite(dataObject));
    }
}
