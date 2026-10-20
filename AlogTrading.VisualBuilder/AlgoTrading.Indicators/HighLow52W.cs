using System;
using AlgoTrading.Indicators.Internal;

namespace AlgoTrading.Indicators;

public static class HighLow52W
{
    public static void Calculate(
        ReadOnlySpan<decimal> high, ReadOnlySpan<decimal> low, int period,
        Span<decimal?> high52, Span<decimal?> low52)
    {
        if (period < 1) throw new ArgumentOutOfRangeException(nameof(period));
        int n = high.Length;
        if (low.Length != n)
            throw new ArgumentException("High and low spans must be equal length.");
        if (high52.Length != n || low52.Length != n)
            throw new ArgumentException("Output spans must equal source length.");

        high52.Clear();
        low52.Clear();
        if (n < period) return;

        int[] maxDeque = Scratch.RentInt(n);
        int[] minDeque = Scratch.RentInt(n);
        try
        {
            int maxHead = 0, maxTail = 0;
            int minHead = 0, minTail = 0;

            for (int i = 0; i < n; i++)
            {
                while (maxTail > maxHead && high[maxDeque[maxTail - 1]] <= high[i]) maxTail--;
                maxDeque[maxTail++] = i;
                while (maxDeque[maxHead] <= i - period) maxHead++;

                while (minTail > minHead && low[minDeque[minTail - 1]] >= low[i]) minTail--;
                minDeque[minTail++] = i;
                while (minDeque[minHead] <= i - period) minHead++;

                if (i >= period - 1)
                {
                    high52[i] = high[maxDeque[maxHead]];
                    low52[i] = low[minDeque[minHead]];
                }
            }
        }
        finally
        {
            Scratch.Return(maxDeque);
            Scratch.Return(minDeque);
        }
    }
}