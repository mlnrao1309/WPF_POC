using System;
using AlgoTrading.Indicators.Internal;

namespace AlgoTrading.Indicators;

public static class Dema
{
    public static void Calculate(ReadOnlySpan<decimal> source, int period, Span<decimal?> output)
    {
        if (period < 1) throw new ArgumentOutOfRangeException(nameof(period));
        if (output.Length != source.Length)
            throw new ArgumentException("Output length must equal source length.");

        output.Clear();
        int n = source.Length;
        int w1 = period - 1;
        int w2 = 2 * (period - 1);
        if (n <= w2) return;

        var e1 = Scratch.RentNullable(n);
        var e2 = Scratch.RentNullable(n);
        try
        {
            var e1s = e1.AsSpan(0, n);
            Ema.Calculate(source, period, e1s);

            var trimmed = e1s.Slice(w1);
            var e2s = e2.AsSpan(0, n - w1);
            Ema.Calculate(trimmed, period, e2s);

            for (int t = w2; t < n; t++)
            {
                int k = t - w1;
                output[t] = 2m * e1s[t]!.Value - e2s[k]!.Value;
            }
        }
        finally
        {
            Scratch.Return(e1);
            Scratch.Return(e2);
        }
    }
}