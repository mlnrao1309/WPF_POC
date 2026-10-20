using Xunit;
using AlgoTrading.Indicators;

namespace AlgoTrading.Indicators.Tests;

public class DemaTemaHmaTests
{
    [Fact]
    public void DemaProducesValuesAfterDoubleWarmup()
    {
        var src = H.Sinusoid(80, 100m, 10m);
        var o = H.Out(80);
        Dema.Calculate(src, 10, o);
        for (int i = 0; i < 2 * 9; i++) H.Null(o[i], i);
        for (int i = 2 * 9; i < 80; i++) Assert.NotNull(o[i]);
    }

    [Fact]
    public void TemaProducesValuesAfterTripleWarmup()
    {
        var src = H.Sinusoid(120, 100m, 10m);
        var o = H.Out(120);
        Tema.Calculate(src, 10, o);
        for (int i = 0; i < 3 * 9; i++) H.Null(o[i], i);
        for (int i = 3 * 9; i < 120; i++) Assert.NotNull(o[i]);
    }

    [Fact]
    public void HmaProducesValuesAfterWarmup()
    {
        var src = H.Sinusoid(80, 100m, 10m);
        var o = H.Out(80);
        Hma.Calculate(src, 9, o);
        Assert.NotNull(o[79]);
    }

    [Fact]
    public void HmaOnConstantIsConstant()
    {
        var src = H.Const(100, 50m);
        var o = H.Out(100);
        Hma.Calculate(src, 9, o);
        // HMA of a constant series == that constant.
        for (int i = 30; i < 100; i++) H.Approx(50m, o[i]);
    }
}
