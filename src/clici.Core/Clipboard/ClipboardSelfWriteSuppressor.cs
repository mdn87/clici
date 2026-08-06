using System.Security.Cryptography;
using System.Text;

namespace Clici.Core.Clipboard;

public sealed class ClipboardSelfWriteSuppressor
{
    private readonly object _sync = new();
    private PendingWrite? _pendingWrite;

    public void MarkPendingWrite(uint sequenceNumber, string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        lock (_sync)
        {
            _pendingWrite = new PendingWrite(
                sequenceNumber,
                text.Length,
                SHA256.HashData(Encoding.UTF8.GetBytes(text)));
        }
    }

    public bool TryConsume(uint sequenceNumber, string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        lock (_sync)
        {
            if (_pendingWrite is null)
            {
                return false;
            }

            var candidateHash = SHA256.HashData(Encoding.UTF8.GetBytes(text));
            var isMatch = _pendingWrite.SequenceNumber == sequenceNumber &&
                          _pendingWrite.TextLength == text.Length &&
                          CryptographicOperations.FixedTimeEquals(
                              _pendingWrite.TextHash,
                              candidateHash);

            _pendingWrite = null;
            return isMatch;
        }
    }

    public void Clear()
    {
        lock (_sync)
        {
            _pendingWrite = null;
        }
    }

    private sealed record PendingWrite(
        uint SequenceNumber,
        int TextLength,
        byte[] TextHash);
}
