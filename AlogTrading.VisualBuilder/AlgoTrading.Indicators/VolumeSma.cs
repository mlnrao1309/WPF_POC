using System;

namespace AlgoTrading.Indicators;

public static class VolumeSma
{
    public static void Calculate(
        ReadOnlySpan<decimal> volume, int period, Span<decimal?> output)
        => Sma.Calculate(volume, period, output);
}