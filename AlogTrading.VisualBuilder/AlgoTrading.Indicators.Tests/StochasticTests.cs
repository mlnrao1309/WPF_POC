using Xunit;
using AlgoTrading.Indicators;

namespace AlgoTrading.Indicators.Tests;

public class StochasticTests
{
    [Fact]
    public void ValuesInRangeAndAlignment()
    {
        var n = 40;
        var h = H.Lin(n, 100m, 1m);
        var l = H.Lin(n, 90m, 1m);
        var c = H.Lin(n, 95m, 1m);
        var k = H.Out(n); var d = H.Out(n);
        Stochastic.Calculate(h, l, c, 14, 3, k, d);
        for (int i = 0; i < 13; i++) H.Null(k[i], i);
        for (int i = 13; i < n; i++) Assert.True(k[i] >= 0m && k[i] <= 100m);
        for (int i = 0; i < 15; i++) H.Null(d[i], i);
        for (int i = 15; i < n; i++) Assert.NotNull(d[i]);
    }
}
