using AlgoTrading.Aggregation;
using AlgoTrading.Models;
using AlgoTrading.Backtest.Runtime;
using Xunit;

namespace AlgoTrading.Backtest.Tests;

public class TimeframeAccessorTests
{
    [Fact]
    public void BaseTimeframe_Resolves_To_Self()
    {
        var series = TestSeries.FromCloses(
            Timeframe.Parse("15m"),
            new DateTime(2024, 1, 1, 9, 15, 0, DateTimeKind.Utc),
            100m, 101m, 102m);
        var cache = new TimeframeCache(series);
        var acc = new TimeframeAccessor(1, series, cache);

        var aligned = acc.Resolve(Timeframe.Parse("15m"));
        Assert.Equal(3, aligned.TfBarCount);
        Assert.Equal(3, aligned.BaseBarCount);
        Assert.Equal(0, aligned.BaseToTfIndex[0]);
        Assert.Equal(1, aligned.BaseToTfIndex[1]);
        Assert.Equal(2, aligned.BaseToTfIndex[2]);
    }

    [Fact]
    public void GetFieldSpan_ReturnsCorrectColumn()
    {
        var series = TestSeries.FromCloses(
            Timeframe.Parse("15m"),
            new DateTime(2024, 1, 1, 9, 15, 0, DateTimeKind.Utc),
            100m, 101m, 102m);
        var cache = new TimeframeCache(series);
        var acc = new TimeframeAccessor(1, series, cache);
        var aligned = acc.Resolve(Timeframe.Parse("15m"));

        var close = aligned.GetFieldSpan("Close");
        Assert.Equal(3, close.Length);
        Assert.Equal(100m, close[0]);
        Assert.Equal(102m, close[2]);
    }

    [Fact]
    public void UnknownField_Throws()
    {
        var series = TestSeries.FromCloses(Timeframe.Parse("15m"),
            new DateTime(2024, 1, 1, 9, 15, 0, DateTimeKind.Utc), 100m);
        var cache = new TimeframeCache(series);
        var acc = new TimeframeAccessor(1, series, cache);
        var aligned = acc.Resolve(Timeframe.Parse("15m"));

        Assert.Throws<InvalidOperationException>(() => aligned.GetFieldSpan("Nope").ToArray());
    }
}