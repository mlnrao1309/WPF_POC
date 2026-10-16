namespace AlgoTrading.Models;

public sealed class InstrumentOhlc
{
    public long InstrumentToken { get; set; }
    public DateTime TimeStamp { get; set; }
    public string TimeFrame { get; set; } = string.Empty;
    public decimal Open { get; set; }
    public decimal High { get; set; }
    public decimal Low { get; set; }
    public decimal Close { get; set; }
    public long Volume { get; set; }
    public long OI { get; set; }
}
