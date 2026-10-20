using Xunit;
using AlgoTrading.Indicators;

namespace AlgoTrading.Indicators.Tests;

public class CciTests
{
    [Fact]
    public void ConstantSeriesIsZero()
    {
        var n = 30;
        var h = H.Const(n, 110m);
        var l = H.Const(n, 100m);
        var c = H.Const(n, 105m);
        var o = H.Out(n);
        Cci.Calculate(h, l, c, 20, o);
        for (int i = 19; i < n; i++) H.Approx(0m, o[i]);
    }
}
