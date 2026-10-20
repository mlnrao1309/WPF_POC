using System;
using AlgoTrading.Indicators.Internal;

namespace AlgoTrading.Indicators;

public static class Hma
{
    public static void Calculate(ReadOnlySpan<decimal> source, int period, Span<decimal?> output)
    {
        if (period < 2) throw new ArgumentOutOfRangeException(nameof(period));
        if (output.Length != source.Length)
            throw new ArgumentException("Output length must equal source length.");

        output.Clear();
        int n = source.Length;
        int half = (int)Math.Round(period / 2.0, MidpointRounding.AwayFromZero);
        if (half < 1) half = 1;
        int sqrt = (int)Math.Round(Math.Sqrt(period), MidpointRounding.AwayFromZero);
        if (sqrt < 1) sqrt = 1;

        int w1 = half - 1;
        int w2 = period - 1;
        int w3 = sqrt - 1;
        int diffStart = Math.Max(w1, w2);
        int totalWarmup = diffStart + w3;
        if (n <= totalWarmup) return;

        var wmaHalf = Scratch.RentNullable(n);
        var wmaFull = Scratch.RentNullable(n);
        var diff = Scratch.RentNullable(n);
        var hmaOut = Scratch.RentNullable(n - diffStart);
        try
        {
            var halfSpan = wmaHalf.AsSpan(0, n);
            var fullSpan = wmaFull.AsSpan(0, n);
            Wma.Calculate(source, half, halfSpan);
            Wma.Calculate(source, period, fullSpan);

            int diffLen = n - diffStart;
            var diffSpan = diff.AsSpan(0, diffLen);
            for (int i = 0; i < diffLen; i++)
            {
                int src = diffStart + i;
                diffSpan[i] = 2m * halfSpan[src]!.Value - fullSpan[src]!.Value;
            }

            var hmaSpan = hmaOut.AsSpan(0, diffLen);
            Wma.Calculate(diffSpan, sqrt, hmaSpan);

            for (int t = totalWarmup; t < n; t++)
            {
                int k = t - diffStart;
                output[t] = hmaSpan[k];
            }
        }
        finally
        {
            Scratch.Return(wmaHalf);
            Scratch.Return(wmaFull);
            Scratch.Return(diff);
            Scratch.Return(hmaOut);
        }
    }
}