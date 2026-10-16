namespace AlgoTrading.Models.MarketData.Entities;

/// <summary>
/// Read-only projection over dbo.vw_InstrumentBars, which joins
/// Instruments_OHLC with MarketWatch (IsDeleted = 0).
/// </summary>
public class InstrumentBarView
{
    public long InstrumentToken { get; set; }
    public decimal Open { get; set; }
    public decimal High { get; set; }
    public decimal Low { get; set; }
    public decimal Close { get; set; }
    public long? Volume { get; set; }
    public long? OI { get; set; }

    public DateTime TimeStamp { get; set; }
    public DateTime? LastUpdated { get; set; }
    public string TimeFrame { get; set; } = string.Empty;

    // MarketWatch columns (nchar(10) padding handled with Trim in the repo)
    public string? Segment { get; set; }
    public string? Exchange { get; set; }
    public DateTime? ExpiresOn { get; set; }
    public string? TradingSymbol { get; set; }
}