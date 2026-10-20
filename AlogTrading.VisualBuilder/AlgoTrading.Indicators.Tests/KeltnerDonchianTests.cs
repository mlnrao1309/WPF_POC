using Xunit;
using AlgoTrading.Indicators;

namespace AlgoTrading.Indicators.Tests;

public class KeltnerDonchianTests
{
    [Fact]
    public void KeltnerSymmetricAroundEma()
    {
        var n = 60;
        var h = H.Sinusoid(n, 105m, 3m);
        var l = H.Sinusoid(n,  95m, 3m);
        var c = H.Sinusoid(n, 100m, 3m);
        var u = H.Out(n); var m = H.Out(n); var lo = H.Out(n);
        KeltnerChannel.Calculate(h, l, c, 20, 10, 2m, u, m, lo);
        for (int i = 30; i < n; i++)
        {
            Assert.NotNull(u[i]);
            Assert.NotNull(m[i]);
            Assert.NotNull(lo[i]);
            decimal half = (u[i]!.Value - lo[i]!.Value) / 2m;
            H.Approx(m[i]!.Value + half, u[i]);
            H.Approx(m[i]!.Value - half, lo[i]);
        }
    }

    [Fact]
    public void DonchianSimpleWindow()
    {
        var h = new decimal[] { 1, 2, 3, 4, 5, 6 };
        var l = new decimal[] { 0, 1, 2, 3, 4, 5 };
        var u = H.Out(6); var m = H.Out(6); var lo = H.Out(6);
        DonchianChannel.Calculate(h, l, 3, u, m, lo);
        H.Approx(3m, u[2]);
        H.Approx(0m, lo[2]);
        H.Approx(6m, u[5]);
        H.Approx(3m, lo[5]);
        H.Approx((u[5]!.Value + lo[5]!.Value) / 2m, m[5]);
    }
}
