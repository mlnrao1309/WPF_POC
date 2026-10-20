using System;

namespace AlgoTrading.Indicators;

public static class Obv
{
    public static void Calculate(
        ReadOnlySpan<decimal> close, ReadOnlySpan<decimal> volume, Span<decimal?> output)
    {
        int n = close.Length;
        if (volume.Length != n)
            throw new ArgumentException("Volume span must equal close length.");
        if (output.Length != n)
            throw new ArgumentException("Output length must equal source length.");

        output.Clear();
        if (n == 0) return;

        decimal obv = 0m;
        output[0] = obv;
        for (int i = 1; i < n; i++)
        {
            if (close[i] > close[i - 1]) obv += volume[i];
            else if (close[i] < close[i - 1]) obv -= volume[i];
            output[i] = obv;
        }
    }
}