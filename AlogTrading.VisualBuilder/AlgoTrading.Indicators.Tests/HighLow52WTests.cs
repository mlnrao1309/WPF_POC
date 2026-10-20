using Xunit;
using AlgoTrading.Indicators;

namespace AlgoTrading.Indicators.Tests;

public class HighLow52WTests
{
    [Fact]
    public void RollingMaxMin()
    {
        var h = new decimal[] { 1, 5, 3, 2, 4, 7 };
        var l = new decimal[] { 0, 2, 1, 1, 3, 6 };
        var hi = H.Out(6); var lo = H.Out(6);
        HighLow52W.Calculate(h, l, 3, hi, lo);

        // Window sizes of 3:
        //   index 2 -> h{1,5,3} max 5, l{0,2,1} min 0
        //   index 3 -> h{5,3,2} max 5, l{2,1,1} min 1
        //   index 5 -> h{2,4,7} max 7, l{1,3,6} min 1
        H.Approx(5m, hi[2]);
        H.Approx(0m, lo[2]);
        H.Approx(5m, hi[3]);
        H.Approx(1m, lo[3]);
        H.Approx(7m, hi[5]);
        H.Approx(1m, lo[5]);
    }
}
