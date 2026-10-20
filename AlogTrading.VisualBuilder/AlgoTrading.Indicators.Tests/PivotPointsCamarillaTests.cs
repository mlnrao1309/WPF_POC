using System;
using Xunit;
using AlgoTrading.Indicators;

namespace AlgoTrading.Indicators.Tests;

public class PivotPointsCamarillaTests
{
    [Fact]
    public void CamarillaFormulaUsesPreviousDay()
    {
        // Prev H=100, L=50, C=75. Range=50, shift=55.
        var h = new decimal[] { 100m, 200m, 105m };
        var l = new decimal[] {  50m, 150m,  55m };
        var c = new decimal[] {  75m, 175m,  80m };
        var dt = new DateTime[]
        {
            new DateTime(2024, 1, 1, 3, 45, 0, DateTimeKind.Utc),
            new DateTime(2024, 1, 2, 3, 45, 0, DateTimeKind.Utc),
            new DateTime(2024, 1, 2, 3, 50, 0, DateTimeKind.Utc)
        };
        var r4 = H.Out(3); var r3 = H.Out(3); var r2 = H.Out(3); var r1 = H.Out(3);
        var s1 = H.Out(3); var s2 = H.Out(3); var s3 = H.Out(3); var s4 = H.Out(3);
        PivotPointsCamarilla.Calculate(h, l, c, dt, r4, r3, r2, r1, s1, s2, s3, s4);

        // Shift = 55. R4 = 75 + 55/2 = 102.5
        H.Approx(102.5m, r4[1]);
        // R3 = 75 + 55/4 = 88.75
        H.Approx(88.75m, r3[1]);
        // R2 = 75 + 55/6 = 84.1666...
        H.Approx(84.1666m, r2[1], 0.001m);
        // R1 = 75 + 55/12 = 79.5833...
        H.Approx(79.5833m, r1[1], 0.001m);
        // S1 = 75 - 55/12 = 70.4166...
        H.Approx(70.4166m, s1[1], 0.001m);
        // S4 = 75 - 55/2 = 47.5
        H.Approx(47.5m, s4[1]);
    }
}
