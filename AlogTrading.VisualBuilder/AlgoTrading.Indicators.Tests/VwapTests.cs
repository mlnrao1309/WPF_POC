using System;
using Xunit;
using AlgoTrading.Indicators;

namespace AlgoTrading.Indicators.Tests;

public class VwapTests
{
    [Fact]
    public void ResetsAtNewIstDay()
    {
        var h = new decimal[] { 10m, 20m, 30m, 40m };
        var l = new decimal[] {  5m, 15m, 25m, 35m };
        var c = new decimal[] { 7.5m, 17.5m, 27.5m, 37.5m };
        var v = H.Const(4, 100m);
        var dt = new DateTime[]
        {
            new DateTime(2024, 1, 1, 3, 45, 0, DateTimeKind.Utc),
            new DateTime(2024, 1, 1, 3, 50, 0, DateTimeKind.Utc),
            new DateTime(2024, 1, 2, 3, 45, 0, DateTimeKind.Utc),
            new DateTime(2024, 1, 2, 3, 50, 0, DateTimeKind.Utc)
        };
        var o = H.Out(4);
        Vwap.Calculate(h, l, c, v, dt, o);
        H.Approx(7.5m, o[0]);
        H.Approx(12.5m, o[1]);
        H.Approx(27.5m, o[2]);
        H.Approx(32.5m, o[3]);
    }
}
