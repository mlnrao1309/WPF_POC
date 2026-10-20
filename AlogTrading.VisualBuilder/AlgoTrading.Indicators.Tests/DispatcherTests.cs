using System;
using Xunit;
using AlgoTrading.Models;
using AlgoTrading.Indicators;

namespace AlgoTrading.Indicators.Tests;

public class DispatcherTests
{
    [Fact]
    public void EveryKindDispatchesAndReturnsDeclaredOutputCount()
    {
        const int n = 300;
        var source = new decimal[n];
        var high = new decimal[n];
        var low = new decimal[n];
        var close = new decimal[n];
        var volume = new decimal[n];
        var dt = new DateTime[n];
        var start = new DateTime(2024, 1, 1, 3, 45, 0, DateTimeKind.Utc);
        for (int i = 0; i < n; i++)
        {
            var baseV = 100m + (decimal)Math.Sin(i * 0.1) * 5m;
            high[i] = baseV + 1m;
            low[i] = baseV - 1m;
            close[i] = baseV;
            source[i] = baseV;
            volume[i] = 1000m;
            dt[i] = start.AddMinutes(i);
        }

        foreach (var d in IndicatorRegistry.All)
        {
            var p = new decimal[d.Parameters.Count];
            for (int i = 0; i < d.Parameters.Count; i++) p[i] = d.Parameters[i].Default;

            var o1 = new decimal?[n]; var o2 = new decimal?[n]; var o3 = new decimal?[n]; var o4 = new decimal?[n];
            var o5 = new decimal?[n]; var o6 = new decimal?[n]; var o7 = new decimal?[n]; var o8 = new decimal?[n];

            IndicatorDispatcher.EvaluateIndicator(d.Kind, source, high, low, close, volume, dt, p,
                o1, o2, o3, o4, o5, o6, o7, o8, out var count);

            Assert.Equal(d.Outputs.Count, count);
        }
    }
}
