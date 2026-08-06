using Clici.Core.Clipboard;

namespace Clici.Core.Tests;

public sealed class ClipboardSelfWriteSuppressorTests
{
    [Fact]
    public void MatchingSequenceAndTextAreSuppressedOnce()
    {
        var suppressor = new ClipboardSelfWriteSuppressor();
        suppressor.MarkPendingWrite(42, "normalized text");

        Assert.True(suppressor.TryConsume(42, "normalized text"));
        Assert.False(suppressor.TryConsume(42, "normalized text"));
    }

    [Fact]
    public void DifferentClipboardChangeIsNotSuppressed()
    {
        var suppressor = new ClipboardSelfWriteSuppressor();
        suppressor.MarkPendingWrite(42, "normalized text");

        Assert.False(suppressor.TryConsume(43, "different text"));
        Assert.False(suppressor.TryConsume(42, "normalized text"));
    }

    [Fact]
    public void SameSequenceWithDifferentTextIsNotSuppressed()
    {
        var suppressor = new ClipboardSelfWriteSuppressor();
        suppressor.MarkPendingWrite(42, "normalized text");

        Assert.False(suppressor.TryConsume(42, "other text"));
    }
}
