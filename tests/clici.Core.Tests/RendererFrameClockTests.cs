using Clici.Core.Navigation;
using Xunit;

namespace Clici.Core.Tests;

public sealed class RendererFrameClockTests
{
    [Fact]
    public void FreshFrameMapsItsAgeOntoTheMonotonicClock()
    {
        Assert.Equal(9_700, RendererFrameClock.ObservedTick(observedAtUnixMilliseconds: 1_000_000, nowUnixMilliseconds: 1_000_300, nowTick: 10_000));
        Assert.Equal(10_000, RendererFrameClock.ObservedTick(1_000_000, 1_000_000, 10_000));
    }

    [Fact]
    public void FrameOlderThanOneLeaseIsRefused()
    {
        Assert.Equal(9_250, RendererFrameClock.ObservedTick(1_000_000, 1_000_750, 10_000));
        Assert.Null(RendererFrameClock.ObservedTick(1_000_000, 1_000_751, 10_000));
    }

    [Fact]
    public void SmallClockSkewCountsAsNowAndLargeSkewIsRefused()
    {
        Assert.Equal(10_000, RendererFrameClock.ObservedTick(1_000_900, 1_000_000, 10_000));
        Assert.Null(RendererFrameClock.ObservedTick(1_001_001, 1_000_000, 10_000));
    }
}
