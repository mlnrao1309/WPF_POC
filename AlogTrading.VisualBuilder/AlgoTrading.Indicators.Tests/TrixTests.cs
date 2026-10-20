using Xunit;
using AlgoTrading.Indicators;

namespace AlgoTrading.Indicators.Tests;

public class TrixTests
{
    [Fact]
    public void ProducesValuesAfterWarmup()
    {
        var n = 120;
        var src = H.Sinusoid(n, 100m, 10m);
        var o = H.Out(n);
        Trix.Calculate(src, 15, 9, o);
        Assert.NotNull(o[n - 1]);
    }
}
