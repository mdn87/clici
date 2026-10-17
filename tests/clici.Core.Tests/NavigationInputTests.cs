using Clici.Core.Navigation;

namespace Clici.Core.Tests;

public sealed class NavigationInputTests
{
    private static readonly NavigationIdentity Identity = new("pane-a", 1, 1, new(AgentKind.ClaudeCode, "session-a"));
    private static readonly NavigationInputContext Context = new(Identity, 1, 10_000);
    private static readonly NavigationKeyInput Up = new(0xBC, NavigationModifiers.Alt | NavigationModifiers.Shift, true);
    private static readonly NavigationKeyInput Down = new(0xBE, NavigationModifiers.Alt | NavigationModifiers.Shift, true);

    private static NavigationInputQueue Enabled(int capacity = 8)
    {
        var queue = new NavigationInputQueue(NavigationBindings.Default, capacity: capacity);
        queue.SetEnabled(true);
        queue.UpdateContext(Context);
        return queue;
    }

    [Fact]
    public void DisabledOrUnknownContextsPassThrough()
    {
        var queue = new NavigationInputQueue(NavigationBindings.Default);
        queue.UpdateContext(Context);
        Assert.Equal(NavigationKeyResult.PassThrough, queue.Handle(Up, 10));
        queue.Handle(Up with { IsDown = false }, 11);
        queue.SetEnabled(true); // Enabling requires a new eligibility observation.
        Assert.Equal(NavigationKeyResult.PassThrough, queue.Handle(Up, 12));
        Assert.Null(queue.Take(13));
    }

    [Theory]
    [InlineData(NavigationModifiers.None)]
    [InlineData(NavigationModifiers.Shift)]
    [InlineData(NavigationModifiers.Alt)]
    [InlineData(NavigationModifiers.Alt | NavigationModifiers.Shift | NavigationModifiers.Control)]
    [InlineData(NavigationModifiers.Alt | NavigationModifiers.Shift | NavigationModifiers.Windows)]
    public void ExtraOrMissingModifiersPassThrough(NavigationModifiers modifiers)
    {
        var queue = Enabled();
        Assert.Equal(NavigationKeyResult.PassThrough, queue.Handle(Up with { Modifiers = modifiers }, 10));
        Assert.Null(queue.Take(10));
    }

    [Fact]
    public void InjectedAndUnrelatedKeysDoNotEnterTheQueue()
    {
        var queue = Enabled();
        Assert.Equal(NavigationKeyResult.PassThrough, queue.Handle(Up with { IsInjected = true }, 10));
        Assert.Equal(NavigationKeyResult.PassThrough, queue.Handle(Up with { VirtualKey = 0x41 }, 10));
        Assert.Equal(NavigationKeyResult.Queued, queue.Handle(Up, 11));
    }

    [Fact]
    public void HeldRepeatsCoalesceButSeparatePressesKeepTheirOrder()
    {
        var queue = Enabled();
        Assert.Equal(NavigationKeyResult.Queued, queue.Handle(Up, 10));
        Assert.Equal(NavigationKeyResult.Consumed, queue.Handle(Up with { IsRepeat = true }, 11));
        Assert.Equal(NavigationKeyResult.Consumed, queue.Handle(Up with { IsDown = false }, 12));
        Assert.Equal(NavigationKeyResult.Queued, queue.Handle(Down, 13));
        queue.Handle(Down with { IsDown = false }, 14);
        Assert.Equal(NavigationKeyResult.Queued, queue.Handle(Up, 15));
        var now = 20;
        foreach (var direction in new[] { NavigationDirection.Previous, NavigationDirection.Next, NavigationDirection.Previous })
        {
            var request = Assert.IsType<NavigationInputRequest>(queue.Take(now));
            Assert.Equal(direction, request.Direction);
            Assert.Null(queue.Take(now)); // Only one request may be in flight.
            Assert.True(queue.Complete(request, ++now));
        }
        Assert.Null(queue.Take(++now));
    }

    [Fact]
    public void HeldKeyThatStartedOutsideScopeIsNotStolenOnEntry()
    {
        var queue = Enabled();
        queue.UpdateContext(null);
        Assert.Equal(NavigationKeyResult.PassThrough, queue.Handle(Up, 10));
        queue.UpdateContext(Context);
        Assert.Equal(NavigationKeyResult.PassThrough, queue.Handle(Up, 11));
        Assert.Equal(NavigationKeyResult.PassThrough, queue.Handle(Up with { IsDown = false }, 12));
        Assert.Equal(NavigationKeyResult.Queued, queue.Handle(Up, 13));
    }

    [Fact]
    public void InstallingMidHoldCannotCreateANewPress()
    {
        var queue = Enabled();
        Assert.Equal(NavigationKeyResult.PassThrough, queue.Handle(Up with { IsRepeat = true }, 10));
        Assert.Null(queue.Take(11));
    }

    [Fact]
    public void ModifierChangeMidHoldDoesNotCreateAnotherGesture()
    {
        var queue = Enabled();
        queue.Handle(Up with { Modifiers = NavigationModifiers.None }, 10);
        Assert.Equal(NavigationKeyResult.PassThrough, queue.Handle(Up, 11));
        Assert.Null(queue.Take(12));
    }

    [Fact]
    public void ModifierChangeBetweenTwoBindingsOnOneKeyDoesNotLeakOrNavigateAgain()
    {
        var queue = new NavigationInputQueue(new(new(0x26, NavigationModifiers.Control), new(0x26, NavigationModifiers.Alt)));
        queue.SetEnabled(true);
        queue.UpdateContext(Context);
        var firstPress = new NavigationKeyInput(0x26, NavigationModifiers.Control, true);
        queue.Handle(firstPress, 10);
        Assert.Equal(NavigationKeyResult.Consumed, queue.Handle(firstPress with { Modifiers = NavigationModifiers.Alt }, 11));
        Assert.Equal(NavigationKeyResult.Consumed, queue.Handle(firstPress with { IsDown = false, Modifiers = NavigationModifiers.None }, 12));
        var first = Assert.IsType<NavigationInputRequest>(queue.Take(13));
        Assert.Equal(NavigationDirection.Previous, first.Direction);
        queue.Complete(first, 14);
        Assert.Null(queue.Take(15));
    }

    [Theory]
    [InlineData(NavigationModifiers.Alt)]
    [InlineData(NavigationModifiers.Shift)]
    [InlineData(NavigationModifiers.None)]
    public void ReleasingModifiersBeforePunctuationDoesNotLeakRepeats(NavigationModifiers remaining)
    {
        var queue = Enabled();
        Assert.Equal(NavigationKeyResult.Queued, queue.Handle(Up, 10));
        Assert.Equal(NavigationKeyResult.Consumed, queue.Handle(Up with { Modifiers = remaining, IsRepeat = true }, 11));
        Assert.Equal(NavigationKeyResult.Consumed, queue.Handle(Up with { Modifiers = remaining, IsDown = false }, 12));
        var request = Assert.IsType<NavigationInputRequest>(queue.Take(13));
        Assert.Equal(NavigationDirection.Previous, request.Direction);
        Assert.True(queue.Complete(request, 14));
        Assert.Null(queue.Take(15));
        Assert.Equal(NavigationKeyResult.PassThrough, queue.Handle(Up with { Modifiers = remaining }, 16));
    }

    [Theory]
    [InlineData(0x26)]
    [InlineData(0x28)]
    public void FormerControlArrowDefaultsPassThrough(int key)
    {
        var queue = Enabled();
        Assert.Equal(NavigationKeyResult.PassThrough, queue.Handle(new(key, NavigationModifiers.Control, true), 10));
        Assert.Null(queue.Take(11));
    }

    [Fact]
    public void QueuedRequestsThatExpireBeforeStartingAreDropped()
    {
        var queue = Enabled();
        queue.Handle(Up, 10);
        Assert.Null(queue.Take(1010));
        queue.Handle(Up with { IsDown = false }, 1011);
        Assert.Equal(NavigationKeyResult.Queued, queue.Handle(Up, 1012));
        Assert.NotNull(queue.Take(1013));
    }

    [Fact]
    public void ShortenedLeaseAndInvalidIdentityCannotAuthorizeInput()
    {
        var queue = Enabled();
        queue.UpdateContext(Context with { Identity = Identity with { Session = new(AgentKind.ClaudeCode, "") } });
        Assert.Equal(NavigationKeyResult.PassThrough, queue.Handle(Up, 10));
        queue.Handle(Up with { IsDown = false }, 11);
        queue.UpdateContext(Context);
        queue.Handle(Up, 12);
        var request = Assert.IsType<NavigationInputRequest>(queue.Take(13));
        queue.UpdateContext(Context with { ValidUntilMilliseconds = 14 });
        Assert.False(queue.CanExecute(request, 14));
    }

    [Fact]
    public void FocusChangePassesRemainingKeysThroughAndCancelsOldWork()
    {
        var queue = Enabled();
        queue.Handle(Up, 10);
        var request = Assert.IsType<NavigationInputRequest>(queue.Take(11));
        queue.UpdateContext(Context with { Identity = Identity with { PaneId = "pane-b" } });
        Assert.False(queue.CanExecute(request, 12));
        Assert.Equal(NavigationKeyResult.PassThrough, queue.Handle(Up, 13));
        Assert.Equal(NavigationKeyResult.PassThrough, queue.Handle(Up with { IsDown = false }, 14));
        Assert.False(queue.Complete(request, 15));
        Assert.Null(queue.Take(16));
    }

    [Fact]
    public void DisableAndReenableRequiresFreshContextAndRelease()
    {
        var queue = Enabled();
        queue.Handle(Up, 10);
        var request = Assert.IsType<NavigationInputRequest>(queue.Take(11));
        queue.SetEnabled(false);
        queue.SetEnabled(true);
        queue.UpdateContext(Context);
        Assert.False(queue.CanExecute(request, 12));
        Assert.Equal(NavigationKeyResult.PassThrough, queue.Handle(Up, 13));
        queue.Handle(Up with { IsDown = false }, 14);
        Assert.Equal(NavigationKeyResult.Queued, queue.Handle(Up, 15));
    }

    [Fact]
    public void FreshnessExpiryAndLateCompletionCannotReviveWork()
    {
        var queue = Enabled();
        queue.UpdateContext(Context with { ValidUntilMilliseconds = 30 });
        queue.Handle(Up, 10);
        var request = Assert.IsType<NavigationInputRequest>(queue.Take(11));
        Assert.False(queue.CanExecute(request, 30));
        Assert.Equal(NavigationKeyResult.PassThrough, queue.Handle(Down, 31));
        queue.UpdateContext(Context);
        Assert.False(queue.Complete(request, 32));
        Assert.Null(queue.Take(33));
    }

    [Fact]
    public void MetadataRefreshDoesNotCancelRequestsOrExtendTheirDeadline()
    {
        var queue = Enabled();
        queue.Handle(Up, 10);
        var request = Assert.IsType<NavigationInputRequest>(queue.Take(11));
        queue.UpdateContext(Context with { ValidUntilMilliseconds = 20_000 });
        Assert.True(queue.CanExecute(request, 500));
        Assert.False(queue.CanExecute(request, 1010));
        Assert.False(queue.Complete(request, 1011));
    }

    [Fact]
    public void TimedOutInFlightWorkSuspendsAdmissionUntilFreshContext()
    {
        var queue = Enabled();
        queue.Handle(Up, 10);
        queue.Take(11);
        queue.Handle(Down, 12);
        Assert.Null(queue.Take(1010));
        queue.Handle(Up with { IsDown = false }, 1011);
        Assert.Equal(NavigationKeyResult.PassThrough, queue.Handle(Up, 1012));
    }

    [Fact]
    public void QueueOverflowIsConsumedAndReportedWithoutReplaying()
    {
        var queue = Enabled(capacity: 1);
        queue.Handle(Up, 10);
        Assert.Equal(NavigationKeyResult.QueueFull, queue.Handle(Down, 11));
        Assert.Equal(NavigationKeyResult.Consumed, queue.Handle(Down with { IsDown = false }, 12));
        var first = Assert.IsType<NavigationInputRequest>(queue.Take(13));
        queue.Complete(first, 14);
        Assert.Null(queue.Take(15));
    }

    [Theory]
    [InlineData("manual-scroll")]
    [InlineData("layout")]
    [InlineData("session")]
    [InlineData("eligibility")]
    public void RelevantChangesInvalidateRequests(string change)
    {
        var queue = Enabled();
        queue.Handle(Up, 10);
        var request = Assert.IsType<NavigationInputRequest>(queue.Take(11));
        switch (change)
        {
            case "manual-scroll": queue.Invalidate(); break;
            case "layout": queue.UpdateContext(Context with { Identity = Identity with { LayoutEpoch = 2 } }); break;
            case "session": queue.UpdateContext(Context with { Identity = Identity with { SessionEpoch = 2 } }); break;
            case "eligibility": queue.UpdateContext(Context with { EligibilityRevision = 2 }); break;
        }
        Assert.False(queue.CanExecute(request, 12));
        Assert.False(queue.Complete(request, 13));
    }

    [Fact]
    public void OldCompletionCannotFinishANewerRequest()
    {
        var queue = Enabled();
        queue.Handle(Up, 10);
        var old = Assert.IsType<NavigationInputRequest>(queue.Take(11));
        queue.Invalidate();
        queue.UpdateContext(Context);
        queue.Handle(Down, 12);
        var current = Assert.IsType<NavigationInputRequest>(queue.Take(13));
        Assert.False(queue.Complete(old, 14));
        Assert.True(queue.CanExecute(current, 14));
        Assert.True(queue.Complete(current, 15));
        Assert.False(queue.CanExecute(current, 16));
    }

    [Fact]
    public void BackwardsClockInvalidatesWork()
    {
        var queue = Enabled();
        queue.Handle(Up, 10);
        var request = Assert.IsType<NavigationInputRequest>(queue.Take(11));
        Assert.False(queue.CanExecute(request, 9));
        Assert.False(queue.CanExecute(request, 12));
    }

    [Fact]
    public void BindingConflictsAndUnmodifiedKeysAreRejected()
    {
        var up = NavigationBindings.Default.Previous;
        Assert.Throws<ArgumentException>(() => new NavigationInputQueue(new(up, up)));
        Assert.Throws<ArgumentException>(() => new NavigationInputQueue(new(up with { Modifiers = NavigationModifiers.None }, NavigationBindings.Default.Next)));
        Assert.Throws<ArgumentOutOfRangeException>(() => Enabled(0));
    }
}
