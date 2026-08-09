using Clici.Core.Clipboard;
using Clici.Core.MarginNormalization;

namespace Clici.Core.Tests;

public sealed class ClipboardSelfWriteSuppressorTests
{
    [Fact]
    public void MatchingContentIsSuppressedWithoutSequenceIdentity()
    {
        var suppressor = new ClipboardSelfWriteSuppressor();
        suppressor.MarkPendingWrite("normalized text");

        Assert.True(suppressor.ShouldSuppress("normalized text"));
    }

    [Fact]
    public void DifferentClipboardChangeIsNotSuppressed()
    {
        var suppressor = new ClipboardSelfWriteSuppressor();
        suppressor.MarkPendingWrite("normalized text");

        Assert.False(suppressor.ShouldSuppress("different text"));
    }

    [Fact]
    public void DifferentContentIsNotSuppressed()
    {
        var suppressor = new ClipboardSelfWriteSuppressor();
        suppressor.MarkPendingWrite("normalized text");

        Assert.False(suppressor.ShouldSuppress("other text"));
    }

    [Fact]
    public void LastWrittenContentRemainsSuppressedAfterAnUnrelatedChange()
    {
        var suppressor = new ClipboardSelfWriteSuppressor();
        suppressor.MarkPendingWrite("normalized text");

        Assert.False(suppressor.ShouldSuppress("different text"));
        Assert.True(suppressor.ShouldSuppress("normalized text"));
        Assert.True(suppressor.ShouldSuppress("normalized text"));
    }

    [Fact]
    public void NewWriteReplacesTheLastWrittenFingerprint()
    {
        var suppressor = new ClipboardSelfWriteSuppressor();
        suppressor.MarkPendingWrite("first output");
        suppressor.MarkPendingWrite("second output");

        Assert.False(suppressor.ShouldSuppress("first output"));
        Assert.True(suppressor.ShouldSuppress("second output"));
    }

    [Fact]
    public void ClearingPendingNotificationRetainsLastWrittenProtection()
    {
        var suppressor = new ClipboardSelfWriteSuppressor();
        suppressor.MarkPendingWrite("normalized text");

        suppressor.ClearPending();

        Assert.True(suppressor.ShouldSuppress("normalized text"));
    }

    [Fact]
    public void SuppressorProtectsNormalizerOutputFromReprocessing()
    {
        // The classifier removes the full base margin in a single idempotent
        // pass, so a second normalization of its own output is a no-op. The
        // suppressor still guards against clici reacting to the notification
        // raised by its own write.
        var normalizer = new MarginNormalizer();
        var suppressor = new ClipboardSelfWriteSuppressor();
        var firstPass = normalizer.Normalize("    first\n    second\n    third");

        Assert.Equal(MarginNormalizationStatus.Normalized, firstPass.Status);
        Assert.Equal(
            MarginNormalizationStatus.NotEligible,
            normalizer.Normalize(firstPass.Text).Status);

        suppressor.MarkPendingWrite(firstPass.Text);

        Assert.True(suppressor.ShouldSuppress(firstPass.Text));
    }
}
