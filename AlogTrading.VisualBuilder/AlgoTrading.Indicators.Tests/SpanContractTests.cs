using System;
using Xunit;
using AlgoTrading.Indicators;

namespace AlgoTrading.Indicators.Tests;

public class SpanContractTests
{
    [Fact]
    public void LengthMismatchThrows()
    {
        var src = new decimal[] { 1, 2, 3 };
        var o = new decimal?[5];
        Assert.Throws<ArgumentException>(() => Sma.Calculate(src, 3, o));
    }

    [Fact]
    public void PeriodZeroThrows()
    {
        var src = new decimal[] { 1, 2, 3 };
        var o = new decimal?[3];
        Assert.Throws<ArgumentOutOfRangeException>(() => Sma.Calculate(src, 0, o));
    }

    [Fact]
    public void OutputClearedBeforeWarmup()
    {
        var src = new decimal[] { 1, 2, 3, 4, 5 };
        var o = new decimal?[5];
        // Prefill with garbage to prove Clear() ran.
        for (int i = 0; i < 5; i++) o[i] = 999m;
        Sma.Calculate(src, 3, o);
        H.Null(o[0], 0);
        H.Null(o[1], 1);
        H.Approx(2m, o[2]);
    }
}
