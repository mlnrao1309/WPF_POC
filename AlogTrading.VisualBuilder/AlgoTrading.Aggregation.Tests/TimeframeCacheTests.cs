using AlgoTrading.Models;
using Xunit;

namespace AlgoTrading.Aggregation.Tests;

public class TimeframeCacheTests
{
    [Fact]
    public void Get_RepeatedCall_ReturnsSameInstance()
    {
        var b = TestDataFactory.BuildSession(new DateOnly(2024, 1, 1), 1);
        var cache = new TimeframeCache(b);
        var tf15 = new Timeframe(TimeframeUnit.m, 15);

        var first = cache.Get(tf15);
        var second = cache.Get(tf15);

        Assert.Same(first, second);
    }

    [Fact]
    public void Get_BaseTimeframe_ReturnsBaseDirectly()
    {
        var b = TestDataFactory.BuildSession(new DateOnly(2024, 1, 1), 5);
        var cache = new TimeframeCache(b);

        var result = cache.Get(new Timeframe(TimeframeUnit.m, 5));

        Assert.Same(b, result);
    }

    [Fact]
    public void Get_WithInstrumentToken_MatchesBase()
    {
        var b = TestDataFactory.BuildSession(new DateOnly(2024, 1, 1), 1);
        var cache = new TimeframeCache(b);

        var result = cache.Get(2885u, new Timeframe(TimeframeUnit.m, 5));

        Assert.NotNull(result);
        Assert.Equal(75, result.Count);
    }

    [Fact]
    public void Get_WithMismatchedInstrumentToken_Throws()
    {
        var b = TestDataFactory.BuildSession(new DateOnly(2024, 1, 1), 1);
        var cache = new TimeframeCache(b);

        Assert.Throws<InvalidOperationException>(
            () => cache.Get(9999u, new Timeframe(TimeframeUnit.m, 5)));
    }

    [Fact]
    public void Get_DifferentTimeframes_ProduceDifferentInstances()
    {
        var b = TestDataFactory.BuildSession(new DateOnly(2024, 1, 1), 1);
        var cache = new TimeframeCache(b);

        var r15 = cache.Get(new Timeframe(TimeframeUnit.m, 15));
        var r25 = cache.Get(new Timeframe(TimeframeUnit.m, 25));

        Assert.NotSame(r15, r25);
        Assert.Equal(25, r15.Count);
        Assert.Equal(15, r25.Count);
    }

    [Fact]
    public void Get_MultipleTimeframes_CachesEachIndependently()
    {
        var b = TestDataFactory.BuildSession(new DateOnly(2024, 1, 1), 1);
        var cache = new TimeframeCache(b);

        var tf5  = new Timeframe(TimeframeUnit.m, 5);
        var tf15 = new Timeframe(TimeframeUnit.m, 15);

        var a1 = cache.Get(tf5);
        var b1 = cache.Get(tf15);
        var a2 = cache.Get(tf5);
        var b2 = cache.Get(tf15);

        Assert.Same(a1, a2);
        Assert.Same(b1, b2);
        Assert.NotSame(a1, b1);
    }
}
