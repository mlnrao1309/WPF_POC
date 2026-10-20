using System;
using AlgoTrading.Indicators.Internal;

namespace AlgoTrading.Indicators;

public static class Tema
{
    public static void Calculate(ReadOnlySpan<decimal> source, int period, Span<decimal?> output)
    {
        if (period < 1) throw new ArgumentOutOfRangeException(nameof(period));
        if (output.Length != source.Length)
            throw new ArgumentException("Output length must equal source length.");

        output.Clear();
        int n = source.Length;
        int w = period - 1;
        int w3 = 3 * w;
        if (n <= w3) return;

        var e1 = Scratch.RentNullable(n);
        var e2 = Scratch.RentNullable(n);
        var e3 = Scratch.RentNullable(n);
        try
        {
            var e1s = e1.AsSpan(0, n);
            var e2s = e2.AsSpan(0, n - w);
            var e3s = e3.AsSpan(0, n - 2 * w);

            Ema.Calculate(source, period, e1s);
            Ema.Calculate(e1s.Slice(w), period, e2s);
            Ema.Calculate(e2s.Slice(w), period, e3s);

            for (int t = w3; t < n; t++)
            {
                decimal v1 = e1s[t]!.Value;
                decimal v2 = e2s[t - w]!.Value;
                decimal v3 = e3s[t - 2 * w]!.Value;
                output[t] = 3m * v1 - 3m * v2 + v3;
            }
        }
        finally
        {
            Scratch.Return(e1);
            Scratch.Return(e2);
            Scratch.Return(e3);
        }
    }
}