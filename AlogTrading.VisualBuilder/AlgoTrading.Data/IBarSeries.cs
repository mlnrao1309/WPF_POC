using AlgoTrading.Models;
using SysDateTime = System.DateTime;

namespace AlgoTrading.Data;

public interface IBarSeries
{
    uint InstrumentToken { get; }
    string Symbol { get; }
    Timeframe Timeframe { get; }
    SysDateTime From { get; }
    SysDateTime To { get; }
    int Count { get; }

    ReadOnlyMemory<decimal> Open { get; }
    ReadOnlyMemory<decimal> High { get; }
    ReadOnlyMemory<decimal> Low { get; }
    ReadOnlyMemory<decimal> Close { get; }
    ReadOnlyMemory<decimal> Volume { get; }
    ReadOnlyMemory<decimal> OI { get; }
    ReadOnlyMemory<SysDateTime> DateTime { get; }
}