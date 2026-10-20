using System;

namespace AlgoTrading.Indicators;

public static class Wma
{
    public static void Calculate(ReadOnlySpan<decimal> source, int period, Span<decimal?> output)
    {
        if (period < 1) throw new ArgumentOutOfRangeException(nameof(period));
        if (output.Length != source.Length)
            throw new ArgumentException("Output length must equal source length.");

        output.Clear();
        if (source.Length < period) return;

        decimal denom = (decimal)period * (period + 1) / 2m;

        decimal sum = 0m;
        decimal windowSum = 0m;
        for (int i = 0; i < period; i++)
        {
            sum += source[i] * (i + 1);
            windowSum += source[i];
        }
        output[period - 1] = sum / denom;

        for (int i = period; i < source.Length; i++)
        {
            decimal prevWma = output[i - 1]!.Value;
            decimal prevWindowSum = windowSum;
            windowSum += source[i] - source[i - period];
            output[i] = prevWma + (period * source[i] - prevWindowSum) / denom;
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

        decimal denom = (decimal)period * (period + 1) / 2m;

        decimal sum = 0m;
        decimal windowSum = 0m;
        for (int i = 0; i < period; i++)
        {
            decimal v = source[firstValid + i]!.Value;
            sum += v * (i + 1);
            windowSum += v;
        }
        output[firstValid + period - 1] = sum / denom;

        for (int i = firstValid + period; i < source.Length; i++)
        {
            decimal prevWma = output[i - 1]!.Value;
            decimal prevWindowSum = windowSum;
            windowSum += source[i]!.Value - source[i - period]!.Value;
            output[i] = prevWma + (period * source[i]!.Value - prevWindowSum) / denom;
        }
    }
}
