using Xunit;
using AlgoTrading.Indicators;

namespace AlgoTrading.Indicators.Tests;

public class WmaTests
{
    [Fact]
    public void LinearWeights()
    {
        // period=3: (1*1 + 2*2 + 3*3)/(1+2+3) = 14/6
        var src = new decimal[] { 1, 2, 3, 4, 5 };
        var o = H.Out(5);
        Wma.Calculate(src, 3, o);
        H.Null(o[0], 0);
        H.Null(o[1], 1);
        H.Approx(14m / 6m, o[2]);
        // next: (2*1 + 3*2 + 4*3)/6 = 20/6
        H.Approx(20m / 6m, o[3]);
        // next: (3*1 + 4*2 + 5*3)/6 = 26/6
        H.Approx(26m / 6m, o[4]);
    }
}
