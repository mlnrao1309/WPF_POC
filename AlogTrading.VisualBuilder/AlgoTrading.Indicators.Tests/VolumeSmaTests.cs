using Xunit;
using AlgoTrading.Indicators;

namespace AlgoTrading.Indicators.Tests;

public class VolumeSmaTests
{
    [Fact]
    public void MatchesSma()
    {
        var v = new decimal[] { 10m, 20m, 30m, 40m, 50m };
        var o = H.Out(5);
        VolumeSma.Calculate(v, 3, o);
        H.Null(o[0], 0);
        H.Null(o[1], 1);
        H.Approx(20m, o[2]);
        H.Approx(30m, o[3]);
        H.Approx(40m, o[4]);
    }
}
