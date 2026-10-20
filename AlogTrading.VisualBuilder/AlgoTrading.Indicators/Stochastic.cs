using System;

namespace AlgoTrading.Indicators;

public static class Stochastic
{
    public static void Calculate(
        ReadOnlySpan<decimal> high, ReadOnlySpan<decimal> low, ReadOnlySpan<decimal> close,
        int kPeriod, int dPeriod,
        Span<decimal?> kOut, Span<decimal?> dOut)
    {
        if (kPeriod < 1 || dPeriod < 1)
            throw new ArgumentOutOfRangeException(nameof(kPeriod));
        int n = close.Length;
        if (high.Length != n || low.Length != n)
            throw new ArgumentException("OHLC spans must be equal length.");
        if (kOut.Length != n || dOut.Length != n)
            throw new ArgumentException("Output spans must equal source length.");

        kOut.Clear();
        dOut.Clear();
        if (n < kPeriod) return;

        for (int i = kPeriod - 1; i < n; i++)
        {
            decimal hh = high[i], ll = low[i];
            for (int j = i - kPeriod + 1; j < i; j++)
            {
                if (high[j] > hh) hh = high[j];
                if (low[j] < ll) ll = low[j];
            }
            decimal range = hh - ll;
            kOut[i] = range == 0m ? 0m : 100m * (close[i] - ll) / range;
        }

        int dStart = kPeriod - 1 + dPeriod - 1;
        if (n <= dStart) return;

        decimal sum = 0m;
        for (int i = kPeriod - 1; i <= dStart; i++) sum += kOut[i]!.Value;
        dOut[dStart] = sum / dPeriod;

        for (int i = dStart + 1; i < n; i++)
        {
            sum += kOut[i]!.Value - kOut[i - dPeriod]!.Value;
            dOut[i] = sum / dPeriod;
        }
    }
}