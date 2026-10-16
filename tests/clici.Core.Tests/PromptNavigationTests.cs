using Clici.Core.Navigation;

namespace Clici.Core.Tests;

public sealed class PromptNavigationTests
{
    private static readonly NavigationIdentity Identity = new("pane-a", 1, 1);
    private static readonly PromptOccurrence First = new(10, 14, "before:hello/after:first response");
    private static readonly PromptOccurrence Second = new(50, 56, "before:hello/after:second response");
    private static readonly PromptOccurrence Third = new(90, 98, "before:Unicode prompt/after:third response");

    private static NavigationSnapshot Snapshot() => new(Identity, 1, PaneEligibility.Eligible,
        0, 130, true, true, true, 80, true, [First, Second, Third]);

    [Fact]
    public void FirstUpFromLiveReachesLatestEvenWhenItsStartIsAlreadyVisible()
    {
        var result = PromptNavigation.Decide(Snapshot(), NavigationDirection.Previous);
        Assert.Equal(NavigationAction.Jump, result.Action);
        Assert.Equal(Third, result.Target);
    }

    [Fact]
    public void SecondUpAcrossAppendUsesVerifiedOccurrenceRatherThanViewportTopOrRevision()
    {
        var refreshed = Snapshot() with { ObservationRevision = 2, CaptureEnd = 150, AtLiveOutput = false };
        var cursor = new NavigationCursor(Identity, Third);
        Assert.Equal(Second, PromptNavigation.Decide(refreshed, NavigationDirection.Previous, cursor).Target);
    }

    [Fact]
    public void DuplicatePromptTextDoesNotCollapseDistinctOccurrences()
    {
        var cursor = new NavigationCursor(Identity, First);
        Assert.Equal(Second, PromptNavigation.Decide(Snapshot(), NavigationDirection.Next, cursor).Target);
    }

    [Fact]
    public void NewlyAppendedPromptAfterLastJumpIsNextRatherThanReturningToLive()
    {
        var fourth = new PromptOccurrence(140, 150, "before:next question/after:fourth response");
        var snapshot = Snapshot() with { ObservationRevision = 2, CaptureEnd = 180, Prompts = [First, Second, Third, fourth] };
        Assert.Equal(fourth, PromptNavigation.Decide(snapshot, NavigationDirection.Next, new(Identity, Third)).Target);
    }

    [Fact]
    public void NextPastNewestReturnsToLiveAndDownAtLiveIsNoOp()
    {
        Assert.Equal(NavigationAction.ReturnToLive, PromptNavigation.Decide(Snapshot(),
            NavigationDirection.Next, new(Identity, Third)).Action);
        Assert.Equal(NavigationAction.NoOp, PromptNavigation.Decide(Snapshot(), NavigationDirection.Next).Action);
    }

    [Fact]
    public void OldestRetainedPromptIsNoOpOnlyAtTheTrueHistoryStart()
    {
        var cursor = new NavigationCursor(Identity, First);
        Assert.Equal(NavigationAction.NoOp, PromptNavigation.Decide(Snapshot(), NavigationDirection.Previous, cursor).Action);
        var bounded = Snapshot() with { CaptureStart = 5, ReachesHistoryStart = false };
        Assert.Equal(NavigationAction.CaptureLimit, PromptNavigation.Decide(bounded, NavigationDirection.Previous, cursor).Action);
    }

    [Theory]
    [InlineData(55, 50)]
    [InlineData(70, 50)]
    [InlineData(50, 10)]
    public void UpAfterManualScrollReachesOwningPromptOrPreviousAtItsBeginning(long top, long expected)
    {
        var snapshot = Snapshot() with { ViewportStart = top, AtLiveOutput = false };
        Assert.Equal(expected, PromptNavigation.Decide(snapshot, NavigationDirection.Previous).Target?.Start);
    }

    [Fact]
    public void DownAfterManualScrollReachesFirstPromptBelowViewportTop()
    {
        var snapshot = Snapshot() with { ViewportStart = 50, AtLiveOutput = false };
        Assert.Equal(Third, PromptNavigation.Decide(snapshot, NavigationDirection.Next).Target);
    }

    [Fact]
    public void CaptureEndingBeforeNewerPromptsCannotReturnToLive()
    {
        var snapshot = Snapshot() with { CaptureEnd = 85, ReachesLiveEnd = false, AtLiveOutput = false, Prompts = [First, Second] };
        Assert.Equal(NavigationAction.CaptureLimit, PromptNavigation.Decide(snapshot,
            NavigationDirection.Next, new(Identity, Second)).Action);
        Assert.Equal(NavigationAction.CaptureLimit, PromptNavigation.Decide(snapshot, NavigationDirection.Next).Action);
    }

    [Fact]
    public void LiveUpCannotSelectAnOlderPromptFromATruncatedTail()
    {
        var snapshot = Snapshot() with { CaptureEnd = 85, ReachesLiveEnd = false, Prompts = [First, Second] };
        Assert.Equal(NavigationAction.CaptureLimit, PromptNavigation.Decide(snapshot, NavigationDirection.Previous).Action);
    }

    [Fact]
    public void LongResponseWithoutCapturedOwnerReportsCaptureLimit()
    {
        var snapshot = Snapshot() with { CaptureStart = 100, ReachesHistoryStart = false, ViewportStart = 110, Prompts = [] };
        Assert.Equal(NavigationAction.CaptureLimit, PromptNavigation.Decide(snapshot, NavigationDirection.Previous).Action);
    }

    [Fact]
    public void EmptyCompleteHistoryHasNoPreviousPrompt()
    {
        Assert.Equal(NavigationAction.NoOp, PromptNavigation.Decide(Snapshot() with { Prompts = [] }, NavigationDirection.Previous).Action);
    }

    [Theory]
    [InlineData(PaneEligibility.Unknown)]
    [InlineData(PaneEligibility.Ineligible)]
    public void UnsupportedPanePassesInputThroughBeforeAnyNavigation(PaneEligibility eligibility)
    {
        var snapshot = Snapshot() with { Eligibility = eligibility };
        Assert.Equal(NavigationAction.PassThrough, PromptNavigation.Decide(snapshot, NavigationDirection.Previous).Action);
    }

    [Theory]
    [InlineData("pane-b", 1, 1)]
    [InlineData("pane-a", 2, 1)]
    [InlineData("pane-a", 1, 2)]
    public void PaneSessionOrLayoutChangeRefusesStaleCursor(string pane, long session, long layout)
    {
        var snapshot = Snapshot() with { Identity = new(pane, session, layout) };
        var decision = PromptNavigation.Decide(snapshot, NavigationDirection.Previous, new(Identity, Third));
        Assert.Equal(NavigationAction.Refused, decision.Action);
        Assert.Equal(NavigationReason.StaleCursor, decision.Reason);
    }

    [Fact]
    public void ReusedPositionWithChangedContextDoesNotRevalidateCursor()
    {
        var snapshot = Snapshot() with { Prompts = [First, Second, Third with { ContextKey = "replacement" }] };
        Assert.Equal(NavigationAction.Refused, PromptNavigation.Decide(snapshot,
            NavigationDirection.Previous, new(Identity, Third)).Action);
    }

    [Fact]
    public void CursorOutsideNewCaptureCannotBeRevalidatedByMatchingText()
    {
        var snapshot = Snapshot() with { CaptureStart = 40, ReachesHistoryStart = false, Prompts = [Second, Third] };
        Assert.Equal(NavigationAction.Refused, PromptNavigation.Decide(snapshot,
            NavigationDirection.Next, new(Identity, First)).Action);
    }

    [Fact]
    public void UncertainOrPartialMessageBoundariesRefuseNavigation()
    {
        Assert.Equal(NavigationAction.Refused, PromptNavigation.Decide(Snapshot() with { BoundariesComplete = false },
            NavigationDirection.Previous).Action);
    }

    [Fact]
    public void UnknownViewportOutsideCaptureCannotPickAnOwner()
    {
        var snapshot = Snapshot() with { AtLiveOutput = false, ViewportStart = 150 };
        Assert.Equal(NavigationAction.CaptureLimit, PromptNavigation.Decide(snapshot, NavigationDirection.Previous).Action);
    }

    [Fact]
    public void UnorderedOverlappingOrOutOfBoundsOccurrencesAreRefused()
    {
        PromptOccurrence[][] invalid = [[Second, First], [First, First], [new(5, 15, "overlap"), First],
            [new(-1, 5, "outside")], [new(120, 140, "partial")], [new(50, 50, "empty")], [new(50, 60, "")]];
        foreach (var prompts in invalid)
        {
            Assert.Equal(NavigationAction.Refused, PromptNavigation.Decide(Snapshot() with { Prompts = prompts },
                NavigationDirection.Previous).Action);
        }
    }
}
