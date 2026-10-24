using AlgoTrading.Models;
using Xunit;

namespace AlgoTrading.Aggregation.Tests;

public sealed class SpecialSessionAggregationTests
{
    [Fact]
    public void EveningWeekendAndShortenedBuckets_PreserveEveryCandleAndFoldValues()
    {
        // Sunday evening: five five-minute candles produce a full and a short 15m bucket.
        var source = Build("2024-11-03 18:00", 0, 5, 10, 15, 20);
        var result = TimeframeAggregator.Aggregate(source, Timeframe.Parse("15m"));
        Assert.Equal(2, result.Count);
        Assert.Equal(new[] { 100m, 103m }, result.Open.ToArray());
        Assert.Equal(new[] { 104m, 106m }, result.High.ToArray());
        Assert.Equal(new[] { 99m, 102m }, result.Low.ToArray());
        Assert.Equal(new[] { 102.5m, 104.5m }, result.Close.ToArray());
        Assert.Equal(new[] { 6m, 9m }, result.Volume.ToArray());
        Assert.Equal(new[] { 12m, 14m }, result.OI.ToArray());
        Assert.Equal(source.DateTime.Span[0], result.DateTime.Span[0]);
        Assert.Equal(source.DateTime.Span[3], result.DateTime.Span[1]);
    }

    [Fact]
    public void Gaps_UseDatetimeBucketsInsteadOfCountingRows()
    {
        var source = Build("2024-11-03 18:00", 0, 5, 30, 35);
        var result = TimeframeAggregator.Aggregate(source, Timeframe.Parse("15m"));
        Assert.Equal(2, result.Count);
        Assert.Equal(new[] { 3m, 7m }, result.Volume.ToArray());
        Assert.Equal(source.DateTime.Span[2], result.DateTime.Span[1]);
    }

    [Theory]
    [InlineData("D", 2)]
    [InlineData("W", 1)]
    [InlineData("M", 1)]
    public void DatePeriods_KeepWeekendCandlesAndPartialPeriods(string timeframe, int expected)
    {
        var source = Build("2024-11-02 18:00", 0, 5, 1440, 1445);
        var result = TimeframeAggregator.Aggregate(source, Timeframe.Parse(timeframe));
        Assert.Equal(expected, result.Count);
        Assert.Equal(10m, result.Volume.ToArray().Sum());
        Assert.Equal(100m, result.Open.Span[0]);
        Assert.Equal(103.5m, result.Close.Span[^1]);
    }

    [Fact]
    public void EachIstDate_UsesItsOwnFirstSuppliedDatetime()
    {
        var source = Build("2024-11-02 23:50", 0, 5, 20, 25);
        var result = TimeframeAggregator.Aggregate(source, Timeframe.Parse("15m"));
        Assert.Equal(2, result.Count);
        Assert.Equal(source.DateTime.Span[2], result.DateTime.Span[1]);
        Assert.Equal(10m, result.Volume.ToArray().Sum());
    }

    private static TestBarSeries Build(string localStart, params int[] minutes)
    {
        var start = DateTime.ParseExact(localStart, "yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture)
            .AddMinutes(-330);
        int count = minutes.Length;
        return new TestBarSeries(2885, "SPECIAL", Timeframe.Parse("5m"),
            Enumerable.Range(0,count).Select(i => 100m+i).ToArray(),
            Enumerable.Range(0,count).Select(i => 102m+i).ToArray(),
            Enumerable.Range(0,count).Select(i => 99m+i).ToArray(),
            Enumerable.Range(0,count).Select(i => 100.5m+i).ToArray(),
            Enumerable.Range(0,count).Select(i => 1m+i).ToArray(),
            Enumerable.Range(0,count).Select(i => 10m+i).ToArray(),
            minutes.Select(m => start.AddMinutes(m)).ToArray());
    }
}
