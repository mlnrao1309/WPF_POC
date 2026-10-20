using System;

namespace AlgoTrading.Indicators;

public static class Sma
{
    public static void Calculate(ReadOnlySpan<decimal> source, int period, Span<decimal?> output)
    {
        if (period < 1) throw new ArgumentOutOfRangeException(nameof(period));
        if (output.Length != source.Length)
            throw new ArgumentException("Output length must equal source length.");

        output.Clear();
        if (source.Length < period) return;

        decimal sum = 0m;
        for (int i = 0; i < period; i++) sum += source[i];
        output[period - 1] = sum / period;

        for (int i = period; i < source.Length; i++)
        {
            sum += source[i] - source[i - period];
            output[i] = sum / period;
        }
    }

    public static void Calculate(ReadOnlySpan<decimal?> source, int period, Span<decimal?> output)
    {
        if (period < 1) throw new ArgumentOutOfRangeException(nameof(period));
        if (output.Length != source.Length)
            throw new ArgumentException("Output length must equal source length.");

        output.Clear();
        int firstValid = 0;
        while (firstValid < source.Length && !source[firstValid].HasValue) firstValid++;
        int validCount = source.Length - firstValid;
        if (validCount < period) return;

        decimal sum = 0m;
        for (int i = firstValid; i < firstValid + period; i++) sum += source[i]!.Value;
        output[firstValid + period - 1] = sum / period;

        for (int i = firstValid + period; i < source.Length; i++)
        {
            sum += source[i]!.Value - source[i - period]!.Value;
            output[i] = sum / period;
        }
    }
}
