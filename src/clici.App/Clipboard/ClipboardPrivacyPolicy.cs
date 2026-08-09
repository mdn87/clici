using System.Runtime.InteropServices;

namespace Clici.App.Clipboard;

/// <summary>
/// The Windows clipboard privacy-policy formats that let a source exclude its
/// content from local history or cross-device cloud synchronization. clici must
/// read and preserve these exactly; it must never add or force them.
/// </summary>
internal sealed record ClipboardPrivacyPolicy(
    uint? CanIncludeInClipboardHistory,
    uint? CanUploadToCloudClipboard,
    bool ExcludeFromMonitorProcessing)
{
    internal const string CanIncludeInClipboardHistoryFormat = "CanIncludeInClipboardHistory";
    internal const string CanUploadToCloudClipboardFormat = "CanUploadToCloudClipboard";
    internal const string ExcludeFromMonitorProcessingFormat =
        "ExcludeClipboardContentFromMonitorProcessing";

    public static ClipboardPrivacyPolicy None { get; } = new(null, null, false);

    /// <summary>
    /// True when the source explicitly set any privacy control. When false, the
    /// source is silent and clici must not synthesize a policy on rewrite.
    /// </summary>
    public bool HasAnyPolicy =>
        CanIncludeInClipboardHistory is not null ||
        CanUploadToCloudClipboard is not null ||
        ExcludeFromMonitorProcessing;

    /// <summary>Reads the three privacy-policy formats from a clipboard item.</summary>
    public static ClipboardPrivacyPolicy FromDataObject(IDataObject dataObject)
    {
        ArgumentNullException.ThrowIfNull(dataObject);

        var history = TryReadDword(dataObject, CanIncludeInClipboardHistoryFormat);
        var cloud = TryReadDword(dataObject, CanUploadToCloudClipboardFormat);

        bool excludeFromMonitoring;
        try
        {
            excludeFromMonitoring =
                dataObject.GetDataPresent(ExcludeFromMonitorProcessingFormat, false);
        }
        catch (ExternalException)
        {
            excludeFromMonitoring = false;
        }

        return new ClipboardPrivacyPolicy(history, cloud, excludeFromMonitoring);
    }

    /// <summary>
    /// Re-applies exactly the source's explicit privacy values to a rewrite.
    /// Silent formats are left absent — clici adds nothing, and never forces
    /// history or cloud inclusion.
    /// </summary>
    public void ApplyTo(DataObject dataObject)
    {
        ArgumentNullException.ThrowIfNull(dataObject);

        if (CanIncludeInClipboardHistory is uint history)
        {
            dataObject.SetData(CanIncludeInClipboardHistoryFormat, false, CreateDword(history));
        }

        if (CanUploadToCloudClipboard is uint cloud)
        {
            dataObject.SetData(CanUploadToCloudClipboardFormat, false, CreateDword(cloud));
        }

        if (ExcludeFromMonitorProcessing)
        {
            dataObject.SetData(ExcludeFromMonitorProcessingFormat, false, CreateDword(0));
        }
    }

    private static uint? TryReadDword(IDataObject dataObject, string format)
    {
        try
        {
            if (!dataObject.GetDataPresent(format, false))
            {
                return null;
            }

            return dataObject.GetData(format, false) switch
            {
                MemoryStream stream when stream.Length >= 4 =>
                    BitConverter.ToUInt32(stream.ToArray(), 0),
                byte[] bytes when bytes.Length >= 4 =>
                    BitConverter.ToUInt32(bytes, 0),
                _ => null
            };
        }
        catch (ExternalException)
        {
            return null;
        }
    }

    private static MemoryStream CreateDword(uint value) =>
        new(BitConverter.GetBytes(value), writable: false);
}
