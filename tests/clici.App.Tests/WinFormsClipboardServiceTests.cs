using Clici.App.Clipboard;

namespace Clici.App.Tests;

public sealed class WinFormsClipboardServiceTests
{
    [Fact]
    public void CreateDataObjectWritesNormalizedTextOnly()
    {
        var dataObject = WinFormsClipboardService.CreateDataObject(
            "normalized",
            ClipboardPrivacyPolicy.None);

        Assert.True(dataObject.TryGetData<string>(
            DataFormats.UnicodeText,
            true,
            out var text));
        Assert.Equal("normalized", text);
    }

    [Fact]
    public void SilentSourcePolicyAddsNoHistoryOrCloudFormats()
    {
        var dataObject = WinFormsClipboardService.CreateDataObject(
            "normalized",
            ClipboardPrivacyPolicy.None);

        Assert.False(dataObject.GetDataPresent(
            ClipboardPrivacyPolicy.CanIncludeInClipboardHistoryFormat,
            false));
        Assert.False(dataObject.GetDataPresent(
            ClipboardPrivacyPolicy.CanUploadToCloudClipboardFormat,
            false));
    }

    [Fact]
    public void ExplicitPrivateSourceValuesArePreservedNotForced()
    {
        var policy = new ClipboardPrivacyPolicy(
            CanIncludeInClipboardHistory: 0,
            CanUploadToCloudClipboard: 0,
            ExcludeFromMonitorProcessing: false);

        var dataObject = WinFormsClipboardService.CreateDataObject("normalized", policy);

        AssertDword(dataObject, ClipboardPrivacyPolicy.CanIncludeInClipboardHistoryFormat, 0);
        AssertDword(dataObject, ClipboardPrivacyPolicy.CanUploadToCloudClipboardFormat, 0);
    }

    [Fact]
    public void ExplicitAllowedSourceValuesArePreserved()
    {
        var policy = new ClipboardPrivacyPolicy(
            CanIncludeInClipboardHistory: 1,
            CanUploadToCloudClipboard: 1,
            ExcludeFromMonitorProcessing: false);

        var dataObject = WinFormsClipboardService.CreateDataObject("normalized", policy);

        AssertDword(dataObject, ClipboardPrivacyPolicy.CanIncludeInClipboardHistoryFormat, 1);
        AssertDword(dataObject, ClipboardPrivacyPolicy.CanUploadToCloudClipboardFormat, 1);
    }

    [Fact]
    public void PrivacyPolicyReadsExplicitHistoryAndCloudValues()
    {
        var source = new DataObject();
        source.SetData(DataFormats.UnicodeText, true, "x");
        SetDword(source, ClipboardPrivacyPolicy.CanIncludeInClipboardHistoryFormat, 0);
        SetDword(source, ClipboardPrivacyPolicy.CanUploadToCloudClipboardFormat, 1);

        var policy = ClipboardPrivacyPolicy.FromDataObject(source);

        Assert.Equal(0u, policy.CanIncludeInClipboardHistory);
        Assert.Equal(1u, policy.CanUploadToCloudClipboard);
        Assert.False(policy.ExcludeFromMonitorProcessing);
        Assert.True(policy.HasAnyPolicy);
    }

    [Fact]
    public void PrivacyPolicyIsSilentWhenNoFormatsPresent()
    {
        var source = new DataObject();
        source.SetData(DataFormats.UnicodeText, true, "x");

        var policy = ClipboardPrivacyPolicy.FromDataObject(source);

        Assert.Null(policy.CanIncludeInClipboardHistory);
        Assert.Null(policy.CanUploadToCloudClipboard);
        Assert.False(policy.HasAnyPolicy);
    }

    [Fact]
    public void PrivacyPolicyFailsClosedOnMalformedHistoryDword()
    {
        // A privacy format is present but its value is too short to be a DWORD.
        // Treating that as "silent" would let a rewrite drop the restriction, so
        // it must be reported as an unreadable policy instead.
        var source = new DataObject();
        source.SetData(DataFormats.UnicodeText, true, "x");
        source.SetData(
            ClipboardPrivacyPolicy.CanIncludeInClipboardHistoryFormat,
            false,
            new MemoryStream([1, 2], writable: false));

        var policy = ClipboardPrivacyPolicy.FromDataObject(source);

        Assert.True(policy.ReadFailed);
        Assert.Null(policy.CanIncludeInClipboardHistory);
    }

    [Fact]
    public void PrivacyPolicySilentSourceDoesNotReportReadFailure()
    {
        var source = new DataObject();
        source.SetData(DataFormats.UnicodeText, true, "x");

        var policy = ClipboardPrivacyPolicy.FromDataObject(source);

        Assert.False(policy.ReadFailed);
    }

    [Fact]
    public void PrivacyPolicyDetectsMonitorProcessingExclusion()
    {
        var source = new DataObject();
        source.SetData(DataFormats.UnicodeText, true, "x");
        SetDword(source, ClipboardPrivacyPolicy.ExcludeFromMonitorProcessingFormat, 0);

        var policy = ClipboardPrivacyPolicy.FromDataObject(source);

        Assert.True(policy.ExcludeFromMonitorProcessing);
        Assert.True(policy.HasAnyPolicy);
    }

    [Fact]
    public void ClassificationOfPlainTextIsSafe()
    {
        var source = new DataObject();
        source.SetData(DataFormats.UnicodeText, true, "hello");

        var classification = ClipboardContentClassification.FromDataObject(source);

        Assert.True(classification.HasNativeUnicodeText);
        Assert.False(classification.HasRichText);
        Assert.False(classification.HasPrimaryNonTextContent);
        Assert.False(classification.HasDisallowedFormat);
    }

    [Fact]
    public void ClassificationFlagsRichHtmlAsDisallowed()
    {
        var source = new DataObject();
        source.SetData(DataFormats.UnicodeText, true, "hello");
        source.SetData(DataFormats.Html, "<p>hello</p>");

        var classification = ClipboardContentClassification.FromDataObject(source);

        Assert.True(classification.HasRichText);
        Assert.True(classification.HasDisallowedFormat);
    }

    [Fact]
    public void ClassificationFlagsFileDropAsPrimaryNonText()
    {
        var source = new DataObject();
        source.SetData(DataFormats.FileDrop, new[] { @"C:\temp\example.txt" });

        var classification = ClipboardContentClassification.FromDataObject(source);

        Assert.True(classification.HasPrimaryNonTextContent);
        Assert.True(classification.HasDisallowedFormat);
    }

    [Fact]
    public void ClassificationFlagsUnknownApplicationFormatAsDisallowed()
    {
        var source = new DataObject();
        source.SetData(DataFormats.UnicodeText, true, "hello");
        source.SetData("application/x-some-editor-model", "opaque");

        var classification = ClipboardContentClassification.FromDataObject(source);

        Assert.True(classification.HasDisallowedFormat);
    }

    private static void SetDword(DataObject dataObject, string format, uint value) =>
        dataObject.SetData(
            format,
            false,
            new MemoryStream(BitConverter.GetBytes(value), writable: false));

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
