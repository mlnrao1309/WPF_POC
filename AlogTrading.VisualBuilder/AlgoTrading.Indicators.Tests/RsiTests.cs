using Xunit;
using AlgoTrading.Indicators;

namespace AlgoTrading.Indicators.Tests;

public class RsiTests
{
    [Fact]
    public void MonotonicUpIs100()
    {
        var src = H.Lin(30);
        var o = H.Out(30);
        Rsi.Calculate(src, 14, o);
        for (int i = 0; i < 14; i++) H.Null(o[i], i);
        for (int i = 14; i < 30; i++) H.Approx(100m, o[i]);
    }

    [Fact]
    public void MonotonicDownIsZero()
    {
        var src = H.Lin(30, 100m, -1m);
        var o = H.Out(30);
        Rsi.Calculate(src, 14, o);
        // avgGain==0, avgLoss>0 => RSI ~ 0 (not exactly zero due to rounding but < 1)
        for (int i = 14; i < 30; i++)
        {
            Assert.NotNull(o[i]);
            Assert.True(o[i]!.Value < 1m);
        }
    }
}
