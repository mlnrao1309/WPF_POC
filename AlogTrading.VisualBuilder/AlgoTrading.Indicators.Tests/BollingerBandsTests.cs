using Xunit;
using AlgoTrading.Indicators;

namespace AlgoTrading.Indicators.Tests;

public class BollingerBandsTests
{
    [Fact]
    public void ConstantSeriesHasZeroWidth()
    {
        var n = 30;
        var src = H.Const(n, 100m);
        var up = H.Out(n); var mid = H.Out(n); var lo = H.Out(n);
        BollingerBands.Calculate(src, 20, 2m, up, mid, lo);
        for (int i = 19; i < n; i++)
        {
            H.Approx(100m, up[i]);
            H.Approx(100m, mid[i]);
            H.Approx(100m, lo[i]);
        }
    }

    [Fact]
    public void BandSymmetricAroundMiddle()
    {
        var n = 40;
        var src = H.Sinusoid(n, 100m, 10m);
        var up = H.Out(n); var mid = H.Out(n); var lo = H.Out(n);
        BollingerBands.Calculate(src, 20, 2m, up, mid, lo);
        for (int i = 19; i < n; i++)
        {
            decimal half = (up[i]!.Value - lo[i]!.Value) / 2m;
            H.Approx(mid[i]!.Value + half, up[i]);
            H.Approx(mid[i]!.Value - half, lo[i]);
        }
    }
}
