using System.Runtime.InteropServices;

namespace Clici.App.Clipboard;

/// <summary>
/// Classifies the native (non-synthesized) formats present on a clipboard item
/// so the coordinator can require a safe text-only bundle before rewriting.
/// Rich content (HTML, RTF, CSV), primary non-text content (images, files,
/// audio, embedded objects), and unknown application formats all disqualify an
/// item from automatic normalization.
/// </summary>
internal sealed record ClipboardContentClassification(
    IReadOnlyList<string> NativeFormats,
    bool HasNativeUnicodeText,
    bool HasRichText,
    bool HasPrimaryNonTextContent,
    bool HasDisallowedFormat)
{
    private static readonly HashSet<string> SafeFormats = new(StringComparer.OrdinalIgnoreCase)
    {
        DataFormats.UnicodeText,
        DataFormats.Text,
        DataFormats.OemText,
        DataFormats.Locale,
        "System.String",
        ClipboardPrivacyPolicy.CanIncludeInClipboardHistoryFormat,
        ClipboardPrivacyPolicy.CanUploadToCloudClipboardFormat,
        ClipboardPrivacyPolicy.ExcludeFromMonitorProcessingFormat,
        CliciWriteMarker.FormatName
    };

    private static readonly HashSet<string> RichTextFormats = new(StringComparer.OrdinalIgnoreCase)
    {
        DataFormats.Html,
        DataFormats.Rtf,
        DataFormats.CommaSeparatedValue
    };

    private static readonly HashSet<string> NonTextFormats = new(StringComparer.OrdinalIgnoreCase)
    {
        DataFormats.Bitmap,
        DataFormats.Dib,
        DataFormats.Tiff,
        DataFormats.EnhancedMetafile,
        DataFormats.MetafilePict,
        DataFormats.FileDrop,
        DataFormats.WaveAudio,
        DataFormats.Riff,
        DataFormats.Palette,
        DataFormats.Dif,
        DataFormats.SymbolicLink,
        DataFormats.Serializable,
        DataFormats.PenData,
        "PNG",
        "JFIF",
        "GIF"
    };

    public static ClipboardContentClassification FromDataObject(IDataObject dataObject)
    {
        ArgumentNullException.ThrowIfNull(dataObject);

        string[] nativeFormats;
        try
        {
            nativeFormats = dataObject.GetFormats(false) ?? [];
        }
        catch (ExternalException)
        {
            nativeFormats = [];
        }

        bool hasNativeUnicodeText;
        try
        {
            hasNativeUnicodeText = dataObject.GetDataPresent(DataFormats.UnicodeText, false);
        }
        catch (ExternalException)
        {
            hasNativeUnicodeText = false;
        }

        var hasRichText = false;
        var hasPrimaryNonTextContent = false;
        var hasDisallowedFormat = false;

        foreach (var format in nativeFormats)
        {
            if (RichTextFormats.Contains(format))
            {
                hasRichText = true;
            }

            if (NonTextFormats.Contains(format))
            {
                hasPrimaryNonTextContent = true;
            }

            if (!SafeFormats.Contains(format))
            {
                hasDisallowedFormat = true;
            }
        }

        return new ClipboardContentClassification(
            nativeFormats,
            hasNativeUnicodeText,
            hasRichText,
            hasPrimaryNonTextContent,
            hasDisallowedFormat);
    }
}
