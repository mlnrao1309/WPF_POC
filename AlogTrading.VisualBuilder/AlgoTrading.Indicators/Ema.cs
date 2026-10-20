using System;

namespace AlgoTrading.Indicators;

public static class Ema
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
        decimal ema = sum / period;
        output[period - 1] = ema;

        decimal alpha = 2m / (period + 1);
        for (int i = period; i < source.Length; i++)
        {
            ema += alpha * (source[i] - ema);
            output[i] = ema;
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
        decimal ema = sum / period;
        output[firstValid + period - 1] = ema;

        decimal alpha = 2m / (period + 1);
        for (int i = firstValid + period; i < source.Length; i++)
        {
            ema += alpha * (source[i]!.Value - ema);
            output[i] = ema;
        }
    }
}
