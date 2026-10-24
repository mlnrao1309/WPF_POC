using AlgoTrading.Models;
using Xunit;

namespace AlgoTrading.Aggregation.Tests;

public class TimeframeAggregatorTests
{
    private static Timeframe TF(TimeframeUnit unit, int count) => new Timeframe(unit, count);

    [Fact]
    public void Aggregate_1m_To_5m_FullSession_Produces75Bars()
    {
        var b = TestDataFactory.BuildSession(new DateOnly(2024, 1, 1), 1);
        var r = TimeframeAggregator.Aggregate(b, TF(TimeframeUnit.m, 5));
        Assert.Equal(75, r.Count);
    }

    [Fact]
    public void Aggregate_1m_To_15m_FullSession_Produces25Bars()
    {
        var b = TestDataFactory.BuildSession(new DateOnly(2024, 1, 1), 1);
        var r = TimeframeAggregator.Aggregate(b, TF(TimeframeUnit.m, 15));
        Assert.Equal(25, r.Count);
    }

    [Fact]
    public void Aggregate_1m_To_25m_FullSession_Produces15Bars()
    {
        var b = TestDataFactory.BuildSession(new DateOnly(2024, 1, 1), 1);
        var r = TimeframeAggregator.Aggregate(b, TF(TimeframeUnit.m, 25));
        Assert.Equal(15, r.Count);
    }

    [Fact]
    public void Aggregate_1m_To_1H_FullSession_PreservesFinalPartialBar()
    {
        var b = TestDataFactory.BuildSession(new DateOnly(2024, 1, 1), 1);
        var r = TimeframeAggregator.Aggregate(b, TF(TimeframeUnit.H, 1));
        Assert.Equal(7, r.Count);
        Assert.Equal(b.Volume.ToArray().Sum(), r.Volume.ToArray().Sum());
    }

    [Fact]
    public void Aggregate_5m_To_25m_FullSession_Produces15Bars()
    {
        var b = TestDataFactory.BuildSession(new DateOnly(2024, 1, 1), 5);
        var r = TimeframeAggregator.Aggregate(b, TF(TimeframeUnit.m, 25));
        Assert.Equal(15, r.Count);
    }

    [Fact]
    public void Aggregate_5m_To_15m_FullSession_Produces25Bars()
    {
        var b = TestDataFactory.BuildSession(new DateOnly(2024, 1, 1), 5);
        var r = TimeframeAggregator.Aggregate(b, TF(TimeframeUnit.m, 15));
        Assert.Equal(25, r.Count);
    }

    [Fact]
    public void Aggregate_6m_To_12m_FullSession_Produces31Bars()
    {
        // The fixture supplies 62 six-minute candles: 31 twelve-minute buckets.
        var b = TestDataFactory.BuildSession(new DateOnly(2024, 1, 1), 6);
        var r = TimeframeAggregator.Aggregate(b, TF(TimeframeUnit.m, 12));
        Assert.Equal(31, r.Count);
    }

    [Fact]
    public void Aggregate_6m_To_18m_PreservesFinalPartialBar()
    {
        // Preserve all supplied candles, including the final partial bucket.
        var b = TestDataFactory.BuildSession(new DateOnly(2024, 1, 1), 6);
        var r = TimeframeAggregator.Aggregate(b, TF(TimeframeUnit.m, 18));
        Assert.Equal(21, r.Count);
        Assert.Equal(b.Volume.ToArray().Sum(), r.Volume.ToArray().Sum());
    }

    [Fact]
    public void Aggregate_6m_To_30m_PreservesFinalPartialBar()
    {
        // Preserve all supplied candles, including the final partial bucket.
        var b = TestDataFactory.BuildSession(new DateOnly(2024, 1, 1), 6);
        var r = TimeframeAggregator.Aggregate(b, TF(TimeframeUnit.m, 30));
        Assert.Equal(13, r.Count);
        Assert.Equal(b.Volume.ToArray().Sum(), r.Volume.ToArray().Sum());
    }

    [Fact]
    public void Aggregate_5m_To_18m_Throws()
    {
        var b = TestDataFactory.BuildSession(new DateOnly(2024, 1, 1), 5);
        Assert.Throws<InvalidOperationException>(
            () => TimeframeAggregator.Aggregate(b, TF(TimeframeUnit.m, 18)));
    }

    [Fact]
    public void Aggregate_IntradayBase_To_IntradayTarget_ThrowsWhenTargetNotMultiple()
    {
        var b = TestDataFactory.BuildSession(new DateOnly(2024, 1, 1), 10);
        Assert.Throws<InvalidOperationException>(
            () => TimeframeAggregator.Aggregate(b, TF(TimeframeUnit.m, 15)));
    }

    [Fact]
    public void Aggregate_1m_To_D_FullSession_Produces1Bar()
    {
        var b = TestDataFactory.BuildSession(new DateOnly(2024, 1, 1), 1);
        var r = TimeframeAggregator.Aggregate(b, TF(TimeframeUnit.D, 1));
        Assert.Equal(1, r.Count);
    }

    [Fact]
    public void Aggregate_D_To_W_AcrossThreeWeeks_Produces3Bars()
    {
        // Week 1 (Mon 2024-01-01 .. Fri 2024-01-05)
        // Week 2 (Mon 2024-01-08 .. Fri 2024-01-12)
        // Week 3 (Mon 2024-01-15 .. Fri 2024-01-19)
        var dates = new[]
        {
            new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 2),
            new DateOnly(2024, 1, 3), new DateOnly(2024, 1, 4),
            new DateOnly(2024, 1, 5),
            new DateOnly(2024, 1, 8), new DateOnly(2024, 1, 9),
            new DateOnly(2024, 1, 10), new DateOnly(2024, 1, 11),
            new DateOnly(2024, 1, 12),
            new DateOnly(2024, 1, 15), new DateOnly(2024, 1, 16),
            new DateOnly(2024, 1, 17), new DateOnly(2024, 1, 18),
            new DateOnly(2024, 1, 19),
        };
        var b = TestDataFactory.BuildDaily(dates);
        var r = TimeframeAggregator.Aggregate(b, TF(TimeframeUnit.W, 1));
        Assert.Equal(3, r.Count);
    }

    [Fact]
    public void Aggregate_D_To_M_AcrossOneYear_Produces12Bars()
    {
        var dates = new List<DateOnly>();
        for (int m = 1; m <= 12; m++)
        {
            dates.Add(new DateOnly(2024, m, 1));
            dates.Add(new DateOnly(2024, m, 15));
        }
        var b = TestDataFactory.BuildDaily(dates.ToArray());
        var r = TimeframeAggregator.Aggregate(b, TF(TimeframeUnit.M, 1));
        Assert.Equal(12, r.Count);
    }

    [Fact]
    public void Aggregate_IntradayBase_To_Weekly_AggregatesThroughDaily()
    {
        // Build two full sessions on consecutive days in the same ISO week.
        var d1 = TestDataFactory.BuildSession(new DateOnly(2024, 1, 1), 1);
        var d2 = TestDataFactory.BuildSession(new DateOnly(2024, 1, 2), 1);

        int n = d1.Count + d2.Count;
        var dt = new DateTime[n];
        var op = new decimal[n];
        var hi = new decimal[n];
        var lo = new decimal[n];
        var cl = new decimal[n];
        var vo = new decimal[n];
        var oi = new decimal[n];

        d1.DateTime.Span.CopyTo(dt.AsSpan(0));
        d1.Open.Span.CopyTo(op.AsSpan(0));
        d1.High.Span.CopyTo(hi.AsSpan(0));
        d1.Low.Span.CopyTo(lo.AsSpan(0));
        d1.Close.Span.CopyTo(cl.AsSpan(0));
        d1.Volume.Span.CopyTo(vo.AsSpan(0));
        d1.OI.Span.CopyTo(oi.AsSpan(0));

        d2.DateTime.Span.CopyTo(dt.AsSpan(d1.Count));
        d2.Open.Span.CopyTo(op.AsSpan(d1.Count));
        d2.High.Span.CopyTo(hi.AsSpan(d1.Count));
        d2.Low.Span.CopyTo(lo.AsSpan(d1.Count));
        d2.Close.Span.CopyTo(cl.AsSpan(d1.Count));
        d2.Volume.Span.CopyTo(vo.AsSpan(d1.Count));
        d2.OI.Span.CopyTo(oi.AsSpan(d1.Count));

        var combined = new TestBarSeries(2885u, "TEST",
            new Timeframe(TimeframeUnit.m, 1), op, hi, lo, cl, vo, oi, dt);

        var r = TimeframeAggregator.Aggregate(combined, TF(TimeframeUnit.W, 1));
        Assert.Equal(1, r.Count);
    }

    [Fact]
    public void Aggregate_EmptySeries_ReturnsEmpty()
    {
        var empty = new TestBarSeries(2885u, "TEST",
            new Timeframe(TimeframeUnit.m, 1),
            Array.Empty<decimal>(), Array.Empty<decimal>(), Array.Empty<decimal>(),
            Array.Empty<decimal>(), Array.Empty<decimal>(), Array.Empty<decimal>(),
            Array.Empty<DateTime>());

        var r = TimeframeAggregator.Aggregate(empty, TF(TimeframeUnit.m, 5));
        Assert.Equal(0, r.Count);
    }

    [Fact]
    public void Aggregate_SameTimeframe_ReturnsBaseInstance()
    {
        var b = TestDataFactory.BuildSession(new DateOnly(2024, 1, 1), 5);
        var r = TimeframeAggregator.Aggregate(b, TF(TimeframeUnit.m, 5));
        Assert.Same(b, r);
    }

    [Fact]
    public void Aggregate_FoldRules_OHLCV_Correct()
    {
        var b = TestDataFactory.BuildSession(new DateOnly(2024, 1, 1), 1);
        var r = TimeframeAggregator.Aggregate(b, TF(TimeframeUnit.m, 5));

        // First 5-minute bar covers base bars 0..4.
        Assert.Equal(b.Open.Span[0], r.Open.Span[0]);
        Assert.Equal(b.Close.Span[4], r.Close.Span[0]);
        //Assert.Equal(b.High.Span[0], r.High.Span[0]); // ascending series
        Assert.Equal(b.High.Span[4], r.High.Span[0]); // ascending series: bucket max is last bar
        decimal expectedVol = 0;
        for (int k = 0; k < 5; k++) expectedVol += b.Volume.Span[k];
        Assert.Equal(expectedVol, r.Volume.Span[0]);

        // Timestamp of first aggregated bar == first base bar's timestamp.
        Assert.Equal(b.DateTime.Span[0], r.DateTime.Span[0]);
    }

    [Fact]
    public void Aggregate_IntradayBase_To_DailyBase_To_Weekly_PreservesSymbolAndToken()
    {
        var b = TestDataFactory.BuildDaily(
            new DateOnly(2024, 1, 1),
            new DateOnly(2024, 1, 2),
            new DateOnly(2024, 1, 3));
        var r = TimeframeAggregator.Aggregate(b, TF(TimeframeUnit.W, 1));
        Assert.Equal(b.InstrumentToken, r.InstrumentToken);
        Assert.Equal(b.Symbol, r.Symbol);
    }
}
