using System;
using Xunit;
using AlgoTrading.Indicators;

namespace AlgoTrading.Indicators.Tests;

public class PivotPointsClassicTests
{
    [Fact]
    public void SingleDayProducesAllNull()
    {
        var h = new decimal[] { 10m, 11m, 12m };
        var l = new decimal[] {  9m, 10m, 11m };
        var c = new decimal[] {  9.5m, 10.5m, 11.5m };
        var dt = new DateTime[]
        {
            new DateTime(2024, 1, 1, 3, 45, 0, DateTimeKind.Utc),
            new DateTime(2024, 1, 1, 3, 50, 0, DateTimeKind.Utc),
            new DateTime(2024, 1, 1, 3, 55, 0, DateTimeKind.Utc)
        };
        var pp = H.Out(3); var r1 = H.Out(3); var r2 = H.Out(3); var r3 = H.Out(3);
        var s1 = H.Out(3); var s2 = H.Out(3); var s3 = H.Out(3);
        PivotPointsClassic.Calculate(h, l, c, dt, pp, r1, r2, r3, s1, s2, s3);
        H.AllNull(pp);
    }

    [Fact]
    public void ClassicFormulaUsesPreviousDay()
    {
        // Day 1: H=100, L=50, C=75.
        // PP=75, R1=100, S1=50, R2=125, S2=25, R3=150, S3=0.
        var h = new decimal[] { 100m, 200m, 105m };
        var l = new decimal[] {  50m, 150m,  55m };
        var c = new decimal[] {  75m, 175m,  80m };
        var dt = new DateTime[]
        {
            new DateTime(2024, 1, 1, 3, 45, 0, DateTimeKind.Utc),
            new DateTime(2024, 1, 2, 3, 45, 0, DateTimeKind.Utc),
            new DateTime(2024, 1, 2, 3, 50, 0, DateTimeKind.Utc)
        };
        var pp = H.Out(3); var r1 = H.Out(3); var r2 = H.Out(3); var r3 = H.Out(3);
        var s1 = H.Out(3); var s2 = H.Out(3); var s3 = H.Out(3);
        PivotPointsClassic.Calculate(h, l, c, dt, pp, r1, r2, r3, s1, s2, s3);
        H.Null(pp[0], 0);
        H.Approx(75m,  pp[1]);
        H.Approx(100m, r1[1]);
        H.Approx(50m,  s1[1]);
        H.Approx(125m, r2[1]);
        H.Approx(25m,  s2[1]);
        H.Approx(150m, r3[1]);
        H.Approx(0m,   s3[1]);
    }
}
