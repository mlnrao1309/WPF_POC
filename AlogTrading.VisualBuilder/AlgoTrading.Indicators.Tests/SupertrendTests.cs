using Xunit;
using AlgoTrading.Indicators;

namespace AlgoTrading.Indicators.Tests;

public class SupertrendTests
{
    [Fact]
    public void DirectionIsPlusOrMinusOne()
    {
        var n = 80;
        var high  = H.Sinusoid(n, 105m, 5m);
        var low   = H.Sinusoid(n, 95m, 5m);
        var close = H.Sinusoid(n, 100m, 5m);
        var line = H.Out(n); var dir = H.Out(n);
        Supertrend.Calculate(high, low, close, 10, 3m, line, dir);
        for (int i = 10; i < n; i++)
        {
            Assert.NotNull(dir[i]);
            Assert.True(dir[i] == 1m || dir[i] == -1m);
            Assert.NotNull(line[i]);
        }
    }
}
