using Xunit;
using AlgoTrading.Indicators;

namespace AlgoTrading.Indicators.Tests;

public class RocMomentumTests
{
    [Fact]
    public void RocPercent()
    {
        var src = new decimal[] { 100m, 110m, 121m };
        var o = H.Out(3);
        Roc.Calculate(src, 1, o);
        H.Null(o[0], 0);
        H.Approx(10m, o[1]);
        H.Approx(10m, o[2]);
    }

    [Fact]
    public void MomentumDiff()
    {
        var src = new decimal[] { 100m, 110m, 121m };
        var o = H.Out(3);
        Momentum.Calculate(src, 1, o);
        H.Null(o[0], 0);
        H.Approx(10m, o[1]);
        H.Approx(11m, o[2]);
    }
}
