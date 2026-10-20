using System;

namespace AlgoTrading.Indicators;

public static class Adl
{
    public static void Calculate(
        ReadOnlySpan<decimal> high, ReadOnlySpan<decimal> low, ReadOnlySpan<decimal> close,
        ReadOnlySpan<decimal> volume, Span<decimal?> output)
    {
        int n = close.Length;
        if (high.Length != n || low.Length != n || volume.Length != n)
            throw new ArgumentException("All input spans must be equal length.");
        if (output.Length != n)
            throw new ArgumentException("Output length must equal source length.");

        output.Clear();
        if (n == 0) return;

        decimal adl = 0m;
        for (int i = 0; i < n; i++)
        {
            decimal range = high[i] - low[i];
            decimal mfm = range == 0m
                ? 0m
                : ((close[i] - low[i]) - (high[i] - close[i])) / range;
            adl += mfm * volume[i];
            output[i] = adl;
        }
    }
}