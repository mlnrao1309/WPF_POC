using AlgoTrading.Data;
using AlgoTrading.Models;

namespace AlgoTrading.Aggregation;

/// <summary>
/// Immutable columnar IBarSeries produced by the aggregator.
/// Arrays are owned; no defensive copy. Allocation is a single
/// group of decimal[] plus one DateTime[].
/// </summary>
internal sealed class ColumnarBarSeries : IBarSeries
{
    public uint InstrumentToken { get; }
    public string Symbol { get; }
    public Timeframe Timeframe { get; }
    public DateTime From { get; }
    public DateTime To { get; }
    public int Count { get; }
    public ReadOnlyMemory<decimal> Open { get; }
    public ReadOnlyMemory<decimal> High { get; }
    public ReadOnlyMemory<decimal> Low { get; }
    public ReadOnlyMemory<decimal> Close { get; }
    public ReadOnlyMemory<decimal> Volume { get; }
    public ReadOnlyMemory<decimal> OI { get; }
    public ReadOnlyMemory<DateTime> DateTime { get; }

    public ColumnarBarSeries(
        uint instrumentToken,
        string symbol,
        Timeframe timeframe,
        decimal[] open,
        decimal[] high,
        decimal[] low,
        decimal[] close,
        decimal[] volume,
        decimal[] oi,
        DateTime[] dateTime)
    {
        InstrumentToken = instrumentToken;
        Symbol = symbol;
        Timeframe = timeframe;
        Open = open;
        High = high;
        Low = low;
        Close = close;
        Volume = volume;
        OI = oi;
        DateTime = dateTime;
        Count = dateTime.Length;
        From = Count > 0 ? dateTime[0] : default;
        To = Count > 0 ? dateTime[Count - 1] : default;
    }
}
