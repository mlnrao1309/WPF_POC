using Xunit;
using AlgoTrading.Indicators;

namespace AlgoTrading.Indicators.Tests;

public class MfiTests
{
    [Fact]
    public void ValuesInRangeWhenAllPositive()
    {
        var n = 30;
        var h = H.Lin(n, 100m, 1m);
        var l = H.Lin(n,  90m, 1m);
        var c = H.Lin(n,  95m, 1m);
        var v = H.Const(n, 1000m);
        var o = H.Out(n);
        Mfi.Calculate(h, l, c, v, 14, o);
        // All rising => MFI = 100.
        for (int i = 14; i < n; i++) H.Approx(100m, o[i]);
    }
}
