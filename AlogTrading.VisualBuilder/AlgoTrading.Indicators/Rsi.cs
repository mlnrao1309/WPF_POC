using System;

namespace AlgoTrading.Indicators;

public static class Rsi
{
    public static void Calculate(ReadOnlySpan<decimal> source, int period, Span<decimal?> output)
    {
        if (period < 1) throw new ArgumentOutOfRangeException(nameof(period));
        if (output.Length != source.Length)
            throw new ArgumentException("Output length must equal source length.");

        output.Clear();
        int n = source.Length;
        if (n <= period) return;

        decimal gainSum = 0m, lossSum = 0m;
        for (int i = 1; i <= period; i++)
        {
            decimal diff = source[i] - source[i - 1];
            if (diff >= 0m) gainSum += diff;
            else lossSum -= diff;
        }

        decimal avgGain = gainSum / period;
        decimal avgLoss = lossSum / period;

        output[period] = avgLoss == 0m
            ? 100m
            : 100m - 100m / (1m + avgGain / avgLoss);

        for (int i = period + 1; i < n; i++)
        {
            decimal diff = source[i] - source[i - 1];
            decimal gain = diff > 0m ? diff : 0m;
            decimal loss = diff < 0m ? -diff : 0m;

            avgGain = (avgGain * (period - 1) + gain) / period;
            avgLoss = (avgLoss * (period - 1) + loss) / period;

            output[i] = avgLoss == 0m
                ? 100m
                : 100m - 100m / (1m + avgGain / avgLoss);
        }
    }
}