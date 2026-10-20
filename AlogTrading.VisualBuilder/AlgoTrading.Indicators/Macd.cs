using System;
using AlgoTrading.Indicators.Internal;

namespace AlgoTrading.Indicators;

public static class Macd
{
    public static void Calculate(
        ReadOnlySpan<decimal> source,
        int fast, int slow, int signal,
        Span<decimal?> macd,
        Span<decimal?> signalOut,
        Span<decimal?> histogram)
    {
        if (fast < 1 || slow < 1 || signal < 1)
            throw new ArgumentOutOfRangeException(nameof(fast));
        int n = source.Length;
        if (macd.Length != n || signalOut.Length != n || histogram.Length != n)
            throw new ArgumentException("All output spans must equal source length.");

        macd.Clear();
        signalOut.Clear();
        histogram.Clear();
        if (n < slow) return;

        var fastArr = Scratch.RentNullable(n);
        var slowArr = Scratch.RentNullable(n);
        try
        {
            var fastSpan = fastArr.AsSpan(0, n);
            var slowSpan = slowArr.AsSpan(0, n);
            Ema.Calculate(source, fast, fastSpan);
            Ema.Calculate(source, slow, slowSpan);

            int macdStart = slow - 1;
            for (int i = macdStart; i < n; i++)
                macd[i] = fastSpan[i]!.Value - slowSpan[i]!.Value;

            int macdLen = n - macdStart;
            var macdTrimmed = macd.Slice(macdStart);
            var sigArr = Scratch.RentNullable(macdLen);
            try
            {
                var sigSpan = sigArr.AsSpan(0, macdLen);
                Ema.Calculate(macdTrimmed, signal, sigSpan);

                int signalStart = macdStart + (signal - 1);
                for (int i = signalStart; i < n; i++)
                {
                    int k = i - macdStart;
                    decimal s = sigSpan[k]!.Value;
                    signalOut[i] = s;
                    histogram[i] = macd[i]!.Value - s;
                }
            }
            finally { Scratch.Return(sigArr); }
        }
        finally
        {
            Scratch.Return(fastArr);
            Scratch.Return(slowArr);
        }
    }
}