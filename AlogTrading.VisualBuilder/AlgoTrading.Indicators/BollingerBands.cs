using System;

namespace AlgoTrading.Indicators;

public static class BollingerBands
{
    public static void Calculate(
        ReadOnlySpan<decimal> source, int period, decimal stdDev,
        Span<decimal?> upper, Span<decimal?> middle, Span<decimal?> lower)
    {
        if (period < 1) throw new ArgumentOutOfRangeException(nameof(period));
        if (stdDev < 0m) throw new ArgumentOutOfRangeException(nameof(stdDev));
        int n = source.Length;
        if (upper.Length != n || middle.Length != n || lower.Length != n)
            throw new ArgumentException("Output spans must equal source length.");

        upper.Clear();
        middle.Clear();
        lower.Clear();
        if (n < period) return;

        decimal sum = 0m, sumSq = 0m;
        for (int i = 0; i < period; i++)
        {
            decimal v = source[i];
            sum += v;
            sumSq += v * v;
        }

        Emit(period - 1, sum, sumSq, period, stdDev, upper, middle, lower);

        for (int i = period; i < n; i++)
        {
            decimal add = source[i];
            decimal rem = source[i - period];
            sum += add - rem;
            sumSq += add * add - rem * rem;
            Emit(i, sum, sumSq, period, stdDev, upper, middle, lower);
        }
    }

    private static void Emit(
        int i, decimal sum, decimal sumSq, int period, decimal stdDev,
        Span<decimal?> upper, Span<decimal?> middle, Span<decimal?> lower)
    {
        decimal m = sum / period;
        decimal variance = sumSq / period - m * m;
        if (variance < 0m) variance = 0m;
        decimal sd = Sqrt(variance);
        middle[i] = m;
        upper[i] = m + stdDev * sd;
        lower[i] = m - stdDev * sd;
    }

    internal static decimal Sqrt(decimal x)
    {
        if (x < 0m) throw new ArgumentOutOfRangeException(nameof(x));
        if (x == 0m) return 0m;
        decimal cur = (decimal)Math.Sqrt((double)x);
        for (int i = 0; i < 4; i++)
        {
            if (cur == 0m) break;
            cur = (cur + x / cur) / 2m;
        }
        return cur;
    }
}