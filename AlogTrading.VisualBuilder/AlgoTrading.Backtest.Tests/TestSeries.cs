using AlgoTrading.Data;
using AlgoTrading.Models;

namespace AlgoTrading.Backtest.Tests;

internal sealed class TestSeries : IBarSeries
{
    public uint InstrumentToken { get; init; } = 1;
    public string Symbol { get; init; } = "TEST";
    public Timeframe Timeframe { get; init; }
    public DateTime From => DateTime.Span.Length > 0 ? DateTime.Span[0] : default;
    public DateTime To => DateTime.Span.Length > 0 ? DateTime.Span[^1] : default;
    public int Count => Open.Length;

    public ReadOnlyMemory<decimal> Open { get; init; }
    public ReadOnlyMemory<decimal> High { get; init; }
    public ReadOnlyMemory<decimal> Low { get; init; }
    public ReadOnlyMemory<decimal> Close { get; init; }
    public ReadOnlyMemory<decimal> Volume { get; init; }
    public ReadOnlyMemory<decimal> OI { get; init; }
    public ReadOnlyMemory<DateTime> DateTime { get; init; }

    public static TestSeries FromCloses(Timeframe tf, DateTime start, params decimal[] closes)
    {
        int n = closes.Length;
        var open = new decimal[n];
        var high = new decimal[n];
        var low = new decimal[n];
        var close = new decimal[n];
        var vol = new decimal[n];
        var oi = new decimal[n];
        var dt = new DateTime[n];

        TimeSpan step = tf.Unit switch
        {
            TimeframeUnit.m => TimeSpan.FromMinutes(tf.ToMinutes()),
            TimeframeUnit.H => TimeSpan.FromMinutes(tf.ToMinutes()),
            TimeframeUnit.D => TimeSpan.FromDays(1),
            _ => TimeSpan.FromDays(1)
        };

        for (int i = 0; i < n; i++)
        {
            open[i] = closes[i];
            high[i] = closes[i] + 1m;
            low[i] = closes[i] - 1m;
            close[i] = closes[i];
            vol[i] = 1000m;
            oi[i] = 0m;
            dt[i] = start.Add(step * i);
        }

        return new TestSeries
        {
            Timeframe = tf,
            Open = open,
            High = high,
            Low = low,
            Close = close,
            Volume = vol,
            OI = oi,
            DateTime = dt
        };
    }
}