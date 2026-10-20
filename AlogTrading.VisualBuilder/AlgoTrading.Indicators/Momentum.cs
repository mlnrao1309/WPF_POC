using System;

namespace AlgoTrading.Indicators;

public static class Momentum
{
    public static void Calculate(ReadOnlySpan<decimal> source, int period, Span<decimal?> output)
    {
        if (period < 1) throw new ArgumentOutOfRangeException(nameof(period));
        if (output.Length != source.Length)
            throw new ArgumentException("Output length must equal source length.");

        output.Clear();
        if (source.Length <= period) return;

        for (int i = period; i < source.Length; i++)
            output[i] = source[i] - source[i - period];
    }
}