using Xunit;
using AlgoTrading.Indicators;

namespace AlgoTrading.Indicators.Tests;

public class EmaTests
{
    [Fact]
    public void SeedIsSmaThenEma()
    {
        var src = new decimal[] { 1, 2, 3, 4, 5, 6 };
        var o = H.Out(6);
        Ema.Calculate(src, 3, o);
        H.Null(o[0], 0);
        H.Null(o[1], 1);
        H.Approx(2m, o[2]);
        H.Approx(3m, o[3]);
        H.Approx(4m, o[4]);
        H.Approx(5m, o[5]);
    }

    [Fact]
    public void ConstantSeriesStaysConstant()
    {
        var src = H.Const(50, 100m);
        var o = H.Out(50);
        Ema.Calculate(src, 10, o);
        for (int i = 9; i < 50; i++) H.Approx(100m, o[i]);
    }

    [Fact]
    public void NullableOverload()
    {
        var src = new decimal?[] { null, null, 1, 2, 3, 4, 5, 6 };
        var o = H.Out(8);
        Ema.Calculate(src, 3, o);
        H.Null(o[0], 0);
        H.Null(o[1], 1);
        H.Null(o[2], 2);
        H.Null(o[3], 3);
        H.Approx(2m, o[4]);
        H.Approx(3m, o[5]);
        H.Approx(4m, o[6]);
        H.Approx(5m, o[7]);
    }
}
