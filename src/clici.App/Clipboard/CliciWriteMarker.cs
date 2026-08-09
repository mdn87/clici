using System.Runtime.InteropServices;

namespace Clici.App.Clipboard;

/// <summary>
/// A private clipboard format that positively identifies clici's own rewrites.
/// It carries a schema version and a per-process random token so a self-write
/// can be told apart from identical content written independently by another
/// application — something the content hash alone cannot do. The SHA-256 hash
/// suppressor remains as a fallback because some clipboard brokers discard
/// private formats.
/// </summary>
internal static class CliciWriteMarker
{
    internal const string FormatName = "application/x-clici-normalized-v1";
    private const int Version = 1;
    private static readonly byte[] Token = BuildToken();

    /// <summary>Stamps the marker onto a rewrite this process is about to publish.</summary>
    public static void Apply(DataObject dataObject)
    {
        ArgumentNullException.ThrowIfNull(dataObject);
        dataObject.SetData(
            FormatName,
            false,
            new MemoryStream(Token, writable: false));
    }

    /// <summary>
    /// True when the item carries this process's marker token, i.e. clici wrote
    /// it. Foreign items and copies whose private format a broker dropped return
    /// false (the hash suppressor covers the latter).
    /// </summary>
    public static bool IsSelfWrite(IDataObject dataObject)
    {
        ArgumentNullException.ThrowIfNull(dataObject);
        return TryReadMarker(dataObject, out var bytes) &&
               bytes is not null &&
               bytes.AsSpan().SequenceEqual(Token);
    }

    private static byte[] BuildToken()
    {
        var random = Guid.NewGuid().ToByteArray();
        var payload = new byte[sizeof(int) + random.Length];
        BitConverter.GetBytes(Version).CopyTo(payload, 0);
        random.CopyTo(payload, sizeof(int));
        return payload;
    }

    private static byte[]? ExtractBytes(object? data) => data switch
    {
        MemoryStream stream => stream.ToArray(),
        byte[] bytes => bytes,
        _ => null
    };

    private static bool TryReadMarker(IDataObject dataObject, out byte[]? bytes)
    {
        bytes = null;
        try
        {
            if (!dataObject.GetDataPresent(FormatName, false))
            {
                return false;
            }

            bytes = ExtractBytes(dataObject.GetData(FormatName, false));
            return bytes is not null;
        }
        catch (ExternalException)
        {
            return false;
        }
    }
}
