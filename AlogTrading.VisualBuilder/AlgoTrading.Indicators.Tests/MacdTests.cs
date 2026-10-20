using Xunit;
using AlgoTrading.Indicators;

namespace AlgoTrading.Indicators.Tests;

public class MacdTests
{
    [Fact]
    public void WarmupAlignment()
    {
        var n = 80;
        var src = H.Lin(n);
        var macd = H.Out(n); var sig = H.Out(n); var hist = H.Out(n);
        Macd.Calculate(src, 12, 26, 9, macd, sig, hist);

        for (int i = 0; i < 25; i++) H.Null(macd[i], i);
        for (int i = 25; i < 25 + 9 - 1; i++) H.Null(sig[i], i);
        for (int i = 33; i < n; i++)
        {
            Assert.NotNull(macd[i]);
            Assert.NotNull(sig[i]);
            Assert.NotNull(hist[i]);
            H.Approx(macd[i]!.Value - sig[i]!.Value, hist[i]);
        }
    }

    [Fact]
    public void ConstantSeriesAllZero()
    {
        var n = 60;
        var src = H.Const(n, 100m);
        var macd = H.Out(n); var sig = H.Out(n); var hist = H.Out(n);
        Macd.Calculate(src, 12, 26, 9, macd, sig, hist);
        for (int i = 33; i < n; i++)
        {
            H.Approx(0m, macd[i]);
            H.Approx(0m, sig[i]);
            H.Approx(0m, hist[i]);
        }
    }
}
