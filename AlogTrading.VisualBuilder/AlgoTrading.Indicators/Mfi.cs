using System;

namespace AlgoTrading.Indicators;

public static class Mfi
{
    public static void Calculate(
        ReadOnlySpan<decimal> high, ReadOnlySpan<decimal> low, ReadOnlySpan<decimal> close,
        ReadOnlySpan<decimal> volume, int period, Span<decimal?> output)
    {
        if (period < 1) throw new ArgumentOutOfRangeException(nameof(period));
        int n = close.Length;
        if (high.Length != n || low.Length != n || volume.Length != n)
            throw new ArgumentException("All input spans must be equal length.");
        if (output.Length != n)
            throw new ArgumentException("Output length must equal source length.");

        output.Clear();
        if (n <= period) return;

        decimal posSum = 0m, negSum = 0m;
        for (int i = 1; i <= period; i++)
        {
            decimal tp = (high[i] + low[i] + close[i]) / 3m;
            decimal prevTp = (high[i - 1] + low[i - 1] + close[i - 1]) / 3m;
            decimal rawMf = tp * volume[i];
            if (tp > prevTp) posSum += rawMf;
            else if (tp < prevTp) negSum += rawMf;
        }

        Emit(period, posSum, negSum, output);

        for (int i = period + 1; i < n; i++)
        {
            int drop = i - period;
            decimal dropTp = (high[drop] + low[drop] + close[drop]) / 3m;
            decimal dropPrevTp = (high[drop - 1] + low[drop - 1] + close[drop - 1]) / 3m;
            decimal dropRaw = dropTp * volume[drop];
            if (dropTp > dropPrevTp) posSum -= dropRaw;
            else if (dropTp < dropPrevTp) negSum -= dropRaw;

            decimal tp = (high[i] + low[i] + close[i]) / 3m;
            decimal prevTp = (high[i - 1] + low[i - 1] + close[i - 1]) / 3m;
            decimal rawMf = tp * volume[i];
            if (tp > prevTp) posSum += rawMf;
            else if (tp < prevTp) negSum += rawMf;

            Emit(i, posSum, negSum, output);
        }
    }

    private static void Emit(int i, decimal posSum, decimal negSum, Span<decimal?> output)
    {
        if (negSum == 0m)
        {
            output[i] = posSum == 0m ? null : 100m;
            return;
        }
        decimal mfr = posSum / negSum;
        output[i] = 100m - 100m / (1m + mfr);
    }
}