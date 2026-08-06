using System.Security.Cryptography;
using System.Text;

namespace Clici.Core.Clipboard;

public sealed class ClipboardSelfWriteSuppressor
{
    private readonly object _sync = new();
    private ContentFingerprint? _pendingWrite;
    private ContentFingerprint? _lastWrittenContent;

    public void MarkPendingWrite(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        lock (_sync)
        {
            var fingerprint = new ContentFingerprint(
                text.Length,
                SHA256.HashData(Encoding.UTF8.GetBytes(text)));
            _pendingWrite = fingerprint;
            _lastWrittenContent = fingerprint;
        }
    }

    public bool ShouldSuppress(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        lock (_sync)
        {
            var candidate = new ContentFingerprint(
                text.Length,
                SHA256.HashData(Encoding.UTF8.GetBytes(text)));
            var pendingContentMatches = _pendingWrite?.Matches(candidate) == true;
            var lastWrittenContentMatches = _lastWrittenContent?.Matches(candidate) == true;

            _pendingWrite = null;
            return pendingContentMatches || lastWrittenContentMatches;
        }
    }

    public void ClearPending()
    {
        lock (_sync)
        {
            _pendingWrite = null;
        }
    }

    private sealed record ContentFingerprint(
        int TextLength,
        byte[] TextHash)
    {
        public bool Matches(ContentFingerprint candidate) =>
            TextLength == candidate.TextLength &&
            CryptographicOperations.FixedTimeEquals(TextHash, candidate.TextHash);
    }
}
