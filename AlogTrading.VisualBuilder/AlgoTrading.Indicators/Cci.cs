using System;

namespace AlgoTrading.Indicators;

public static class Cci
{
    public static void Calculate(
        ReadOnlySpan<decimal> high, ReadOnlySpan<decimal> low, ReadOnlySpan<decimal> close,
        int period, Span<decimal?> output)
    {
        if (period < 1) throw new ArgumentOutOfRangeException(nameof(period));
        int n = close.Length;
        if (high.Length != n || low.Length != n)
            throw new ArgumentException("OHLC spans must be equal length.");
        if (output.Length != n)
            throw new ArgumentException("Output length must equal source length.");

        output.Clear();
        if (n < period) return;

        for (int i = period - 1; i < n; i++)
        {
            decimal sum = 0m;
            for (int j = i - period + 1; j <= i; j++)
                sum += (high[j] + low[j] + close[j]) / 3m;
            decimal mean = sum / period;

            decimal madSum = 0m;
            for (int j = i - period + 1; j <= i; j++)
            {
                decimal tp = (high[j] + low[j] + close[j]) / 3m;
                decimal d = tp - mean;
                if (d < 0m) d = -d;
                madSum += d;
            }
            decimal mad = madSum / period;
            decimal tpI = (high[i] + low[i] + close[i]) / 3m;
            output[i] = mad == 0m ? 0m : (tpI - mean) / (0.015m * mad);
        }
    }
}