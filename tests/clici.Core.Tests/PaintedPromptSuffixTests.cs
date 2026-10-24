using Clici.Core.Navigation;

namespace Clici.Core.Tests;

public sealed class PaintedPromptSuffixTests
{
    private static readonly RenderedPromptBinding[] Bindings =
        Enumerable.Range(1, 4).Select(i => new RenderedPromptBinding($"message-{i}", new string((char)('0' + i), 32))).ToArray();

    private static PaintedPromptMarker Mark(int index, long row, bool end = false) => new(Bindings[index].Nonce, row, end);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DamagedOlderTurnBoundsTheRecentCompleteSuffix(bool endOnly)
    {
        var anchors = RenderedPromptMap.CompleteSuffix(Bindings,
            [Mark(0, 10), Mark(0, 13, true), Mark(1, 18), Mark(1, 21, true), Mark(2, 96, endOnly), Mark(3, 211), Mark(3, 214, true)], 313);
        var map = RenderedPromptMap.Resolve(Bindings, anchors, 313);
        Assert.True(map.Success);
        Assert.False(map.ReachesHistoryStart);
        var prompt = Assert.Single(map.Prompts);
        Assert.Equal("message-4", prompt.MessageId);
        var identity = new NavigationIdentity("pane", 1, 1, new(AgentKind.ClaudeCode, "session"));
        var snapshot = new NavigationSnapshot(identity, 1, PaneEligibility.Eligible, prompt.Start, 313,
            map.ReachesHistoryStart, true, true, 280, true, map.Prompts);
        Assert.Equal(prompt, PromptNavigation.Decide(snapshot, NavigationDirection.Previous).Target);
        Assert.Equal(NavigationAction.CaptureLimit,
            PromptNavigation.Decide(snapshot, NavigationDirection.Previous, new(identity, prompt)).Action);
    }

    [Fact]
    public void MissingInteriorTurnDoesNotSkipToOlderCompleteTurns()
    {
        var anchors = RenderedPromptMap.CompleteSuffix(Bindings,
            [Mark(0, 10), Mark(0, 13, true), Mark(2, 30), Mark(2, 33, true), Mark(3, 40), Mark(3, 43, true)], 100);
        var map = RenderedPromptMap.Resolve(Bindings, anchors, 100);
        Assert.True(map.Success);
        Assert.Equal(new[] { "message-3", "message-4" }, map.Prompts.Select(p => p.MessageId));
        Assert.False(map.ReachesHistoryStart);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingNewestBoundaryStillRefuses(bool endOnly)
    {
        Assert.Empty(RenderedPromptMap.CompleteSuffix(Bindings,
            [Mark(2, 30), Mark(2, 33, true), Mark(3, 40, endOnly)], 100));
        Assert.Empty(RenderedPromptMap.CompleteSuffix(Bindings, [Mark(2, 30), Mark(2, 33, true)], 100));
    }

    [Fact]
    public void DuplicatesAndWrongOrderRefuseEvenBeforeTheGap()
    {
        Assert.Empty(RenderedPromptMap.CompleteSuffix(Bindings,
            [Mark(0, 10), Mark(0, 13, true), Mark(0, 15), Mark(2, 30), Mark(3, 40), Mark(3, 43, true)], 100));
        Assert.Empty(RenderedPromptMap.CompleteSuffix(Bindings,
            [Mark(2, 45), Mark(3, 40), Mark(3, 43, true)], 100));
        Assert.Empty(RenderedPromptMap.CompleteSuffix(Bindings,
            [Mark(0, 25), Mark(1, 20), Mark(3, 40), Mark(3, 43, true)], 100));
    }

    [Fact]
    public void UnknownMarkersAndInvalidNativeIdentityCannotBeDiscarded()
    {
        Assert.Empty(RenderedPromptMap.CompleteSuffix(Bindings,
            [new(new string('f', 32), 10, false), Mark(3, 40), Mark(3, 43, true)], 100));
        var duplicate = Bindings.ToArray();
        duplicate[0] = duplicate[0] with { MessageId = duplicate[3].MessageId };
        Assert.Empty(RenderedPromptMap.CompleteSuffix(duplicate, [Mark(3, 40), Mark(3, 43, true)], 100));
    }

    [Fact]
    public void CompleteHistoryStillReachesItsBeginning()
    {
        var markers = Bindings.SelectMany((b, i) => new[] { Mark(i, i * 10), Mark(i, i * 10 + 3, true) }).Reverse().ToArray();
        var anchors = RenderedPromptMap.CompleteSuffix(Bindings, markers, 100);
        var map = RenderedPromptMap.Resolve(Bindings, anchors, 100);
        Assert.True(map.Success);
        Assert.True(map.ReachesHistoryStart);
        Assert.Equal(4, map.Prompts.Count);
    }
}
