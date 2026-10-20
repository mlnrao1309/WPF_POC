using Xunit;
using AlgoTrading.Indicators;

namespace AlgoTrading.Indicators.Tests;

public class AtrTests
{
    [Fact]
    public void ConstantRangeYieldsConstantAtr()
    {
        var n = 25;
        var high  = H.Const(n, 110m);
        var low   = H.Const(n, 100m);
        var close = H.Const(n, 105m);
        var o = H.Out(n);
        Atr.Calculate(high, low, close, 14, o);
        for (int i = 0; i < 14; i++) H.Null(o[i], i);
        for (int i = 14; i < n; i++) H.Approx(10m, o[i]);
    }
}
