using Xunit;
using AlgoTrading.Indicators;

namespace AlgoTrading.Indicators.Tests;

public class SmaTests
{
    [Fact]
    public void WindowOfThree()
    {
        var src = new decimal[] { 1, 2, 3, 4, 5 };
        var o = H.Out(5);
        Sma.Calculate(src, 3, o);
        H.Null(o[0], 0);
        H.Null(o[1], 1);
        H.Approx(2m, o[2]);
        H.Approx(3m, o[3]);
        H.Approx(4m, o[4]);
    }

    [Fact]
    public void ShortInputAllNull()
    {
        var src = new decimal[] { 1, 2 };
        var o = H.Out(2);
        Sma.Calculate(src, 5, o);
        H.AllNull(o);
    }

    [Fact]
    public void NullableOverloadSkipsLeadingNulls()
    {
        var src = new decimal?[] { null, null, 1, 2, 3, 4, 5 };
        var o = H.Out(7);
        Sma.Calculate(src, 3, o);
        H.Null(o[0], 0);
        H.Null(o[1], 1);
        H.Null(o[2], 2);
        H.Null(o[3], 3);
        H.Approx(2m, o[4]);
        H.Approx(3m, o[5]);
        H.Approx(4m, o[6]);
    }
}
