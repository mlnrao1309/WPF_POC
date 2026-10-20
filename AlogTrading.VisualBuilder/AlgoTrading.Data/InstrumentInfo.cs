using AlgoTrading.Models;

namespace AlgoTrading.Data;

public sealed class InstrumentInfo
{
    public uint Token { get; init; }
    public string Symbol { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public InstrumentType InstrumentType { get; init; }
    public int LotSize { get; init; }
    public decimal TickSize { get; init; }
}