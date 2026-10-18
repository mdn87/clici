using Clici.Core.Navigation;

namespace Clici.Core.Tests;

public sealed class RenderedPromptMapTests
{
    private static readonly RenderedPromptBinding[] Bindings =
    [new("message-1", new string('1', 32)), new("message-2", new string('2', 32))];

    [Fact]
    public void JoinsNativeIdentitiesToCompletePaintedPairsInBufferOrder()
    {
        var result = RenderedPromptMap.Resolve(Bindings,
            [new(Bindings[1].Nonce, 40, 44), new(Bindings[0].Nonce, 3, 6)], 100);
        Assert.True(result.Success);
        Assert.True(result.ReachesHistoryStart);
        Assert.Equal(new[] { "message-1", "message-2" }, result.Prompts.Select(prompt => prompt.MessageId));
        Assert.Equal(4, result.Prompts[0].Start); // The blank anchor precedes the prompt.
        Assert.Equal(6, result.Prompts[0].End);
    }

    [Theory]
    [InlineData("0123456789ABCDEF0123456789ABCDEF")]
    [InlineData("0123456789abcdef0123456789abcdef")] // Claude's plugin painted lowercase before PR #35.
    [InlineData("0123456789aBcDeF0123456789AbCdEf")]
    public void NonceCaseCarriesNoIdentity(string nonce)
    {
        RenderedPromptBinding[] bindings = [new("message-1", nonce)];
        var result = RenderedPromptMap.Resolve(bindings, [new(nonce, 3, 6)], 100);
        Assert.True(result.Success);
        Assert.Equal("message-1", result.Prompts.Single().MessageId);
        Assert.True(RenderedPromptMap.Resolve(bindings, [new(nonce.ToUpperInvariant(), 3, 6)], 100).Success);
    }

    [Fact]
    public void NoncesEqualApartFromCaseAreDuplicates()
    {
        var nonce = "0123456789abcdef0123456789abcdef";
        Assert.False(RenderedPromptMap.Resolve([new("message-1", nonce), new("message-2", nonce.ToUpperInvariant())],
            [new(nonce, 3, 6), new(nonce.ToUpperInvariant(), 10, 14)], 100).Success);
    }

    [Fact]
    public void EvictedPrefixIsACaptureBoundaryAndNeverBecomesAnOldestPromptClaim()
    {
        var result = RenderedPromptMap.Resolve(Bindings, [new(Bindings[1].Nonce, 3, 6)], 100);
        Assert.True(result.Success);
        Assert.False(result.ReachesHistoryStart);
        Assert.Single(result.Prompts);
    }

    [Fact]
    public void MissingNewestOrInteriorPromptRefusesEvenWhenAnOlderPairSurvives()
    {
        Assert.False(RenderedPromptMap.Resolve(Bindings, [new(Bindings[0].Nonce, 3, 6)], 100).Success);
    }

    [Fact]
    public void DuplicatePaintOfOneNativePromptRefusesRatherThanPickingFirst()
    {
        Assert.False(RenderedPromptMap.Resolve(Bindings,
            [new(Bindings[0].Nonce, 3, 6), new(Bindings[0].Nonce, 13, 16), new(Bindings[1].Nonce, 23, 26)], 100).Success);
    }

    [Theory]
    [InlineData(-1, 4)]
    [InlineData(4, 4)]
    [InlineData(4, 5)]
    [InlineData(4, 101)]
    [InlineData(long.MaxValue, 10)]
    public void IncompleteOrOutOfBoundsPaintRefuses(long start, long end)
    {
        Assert.False(RenderedPromptMap.Resolve([Bindings[0]], [new(Bindings[0].Nonce, start, end)], 100).Success);
    }

    [Fact]
    public void ReplayOrderMustMatchNativeConversationOrder()
    {
        Assert.False(RenderedPromptMap.Resolve(Bindings,
            [new(Bindings[1].Nonce, 3, 6), new(Bindings[0].Nonce, 13, 16)], 100).Success);
    }

    [Fact]
    public void DuplicateNativeIdsAndAnchorNoncesRefuse()
    {
        var painted = new PaintedPromptAnchor[] { new(Bindings[0].Nonce, 3, 6), new(Bindings[1].Nonce, 13, 16) };
        Assert.False(RenderedPromptMap.Resolve([Bindings[0], Bindings[1] with { MessageId = "message-1" }], painted, 100).Success);
        Assert.False(RenderedPromptMap.Resolve([Bindings[0], Bindings[1] with { Nonce = Bindings[0].Nonce }], painted, 100).Success);
    }

    [Fact]
    public void UnregisteredAnchorNeverBecomesAUserPrompt()
    {
        Assert.False(RenderedPromptMap.Resolve([Bindings[0]], [new(Bindings[1].Nonce, 3, 6)], 100).Success);
    }

    [Fact]
    public void OverlapAndMalformedNativeIdentityRefuse()
    {
        Assert.False(RenderedPromptMap.Resolve(Bindings,
            [new(Bindings[0].Nonce, 3, 20), new(Bindings[1].Nonce, 13, 16)], 100).Success);
        Assert.False(RenderedPromptMap.Resolve([new("", Bindings[0].Nonce)], [new(Bindings[0].Nonce, 3, 6)], 100).Success);
        Assert.False(RenderedPromptMap.Resolve([new("native-id", "guessed-text")], [], 100).Success);
    }
}
