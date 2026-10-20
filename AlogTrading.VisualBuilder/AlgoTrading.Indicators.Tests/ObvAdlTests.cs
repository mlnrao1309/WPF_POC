using Xunit;
using AlgoTrading.Indicators;

namespace AlgoTrading.Indicators.Tests;

public class ObvAdlTests
{
    [Fact]
    public void ObvAccumulatesSignedVolume()
    {
        var close  = new decimal[] { 10m, 11m, 10m, 12m };
        var volume = new decimal[] { 100m, 100m, 100m, 100m };
        var o = H.Out(4);
        Obv.Calculate(close, volume, o);
        H.Approx(0m, o[0]);
        H.Approx(100m, o[1]);
        H.Approx(0m, o[2]);
        H.Approx(100m, o[3]);
    }

    [Fact]
    public void AdlZeroRangeLeavesAdlUnchanged()
    {
        var h = new decimal[] { 10m, 10m };
        var l = new decimal[] { 10m, 10m };
        var c = new decimal[] { 10m, 10m };
        var v = new decimal[] { 100m, 100m };
        var o = H.Out(2);
        Adl.Calculate(h, l, c, v, o);
        H.Approx(0m, o[0]);
        H.Approx(0m, o[1]);
    }
}
