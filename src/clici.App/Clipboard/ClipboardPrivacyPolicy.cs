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
    bool ExcludeFromMonitorProcessing,
    bool ReadFailed = false)
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

        var readFailed = false;
        var history = TryReadDword(dataObject, CanIncludeInClipboardHistoryFormat, ref readFailed);
        var cloud = TryReadDword(dataObject, CanUploadToCloudClipboardFormat, ref readFailed);

        bool excludeFromMonitoring;
        try
        {
            excludeFromMonitoring =
                dataObject.GetDataPresent(ExcludeFromMonitorProcessingFormat, false);
        }
        catch (ExternalException)
        {
            // Cannot tell whether the source excluded the item. Fail closed by
            // marking the read failed so the coordinator skips the rewrite
            // rather than risk stripping an unobserved restriction.
            excludeFromMonitoring = false;
            readFailed = true;
        }

        return new ClipboardPrivacyPolicy(history, cloud, excludeFromMonitoring, readFailed);
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

    private static uint? TryReadDword(IDataObject dataObject, string format, ref bool readFailed)
    {
        try
        {
            if (!dataObject.GetDataPresent(format, false))
            {
                return null; // Absent: the source is silent about this policy.
            }

            switch (dataObject.GetData(format, false))
            {
                case MemoryStream stream when stream.Length >= 4:
                    return BitConverter.ToUInt32(stream.ToArray(), 0);
                case byte[] bytes when bytes.Length >= 4:
                    return BitConverter.ToUInt32(bytes, 0);
                default:
                    // Present but unreadable: fail closed rather than treat the
                    // restriction as absent.
                    readFailed = true;
                    return null;
            }
        }
        catch (ExternalException)
        {
            readFailed = true;
            return null;
        }
    }

    private static MemoryStream CreateDword(uint value) =>
        new(BitConverter.GetBytes(value), writable: false);
}
