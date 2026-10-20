using Xunit;
using AlgoTrading.Indicators;

namespace AlgoTrading.Indicators.Tests;

public class AdxTests
{
    [Fact]
    public void WarmupAndRange()
    {
        var n = 80;
        var high  = H.Sinusoid(n, 105m, 5m);
        var low   = H.Sinusoid(n, 95m, 5m);
        var close = H.Sinusoid(n, 100m, 5m);
        var adx = H.Out(n); var p = H.Out(n); var m = H.Out(n);
        Adx.Calculate(high, low, close, 14, adx, p, m);
        for (int i = 0; i < 28; i++) H.Null(adx[i], i);
        for (int i = 28; i < n; i++)
        {
            Assert.NotNull(adx[i]);
            Assert.True(adx[i]!.Value >= 0m);
        }
    }
}
