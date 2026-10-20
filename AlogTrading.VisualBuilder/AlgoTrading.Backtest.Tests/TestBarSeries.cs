using System;
using AlgoTrading.Data;
using AlgoTrading.Models;

namespace AlgoTrading.Backtest.Tests;

internal sealed class TestBarSeries : IBarSeries
{
    public uint InstrumentToken { get; }
    public string Symbol { get; }
    public Timeframe Timeframe { get; }
    public int Count => DateTime.Length;
    public DateTime From => DateTime.Length > 0 ? DateTime.Span[0] : default;
    public DateTime To   => DateTime.Length > 0 ? DateTime.Span[DateTime.Length - 1] : default;
    public ReadOnlyMemory<decimal> Open { get; }
    public ReadOnlyMemory<decimal> High { get; }
    public ReadOnlyMemory<decimal> Low { get; }
    public ReadOnlyMemory<decimal> Close { get; }
    public ReadOnlyMemory<decimal> Volume { get; }
    public ReadOnlyMemory<decimal> OI { get; }
    public ReadOnlyMemory<DateTime> DateTime { get; }

    public TestBarSeries(uint token, string symbol, Timeframe tf,
        decimal[] o, decimal[] h, decimal[] l, decimal[] c,
        decimal[] v, decimal[] oi, DateTime[] dt)
    {
        InstrumentToken = token;
        Symbol = symbol;
        Timeframe = tf;
        Open = o; High = h; Low = l; Close = c; Volume = v; OI = oi;
        DateTime = dt;
    }
}