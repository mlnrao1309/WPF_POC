namespace AlgoTrading.Models;

public readonly struct Candle
{
    public DateTime DateTime { get; }
    public decimal Open { get; }
    public decimal High { get; }
    public decimal Low { get; }
    public decimal Close { get; }
    public decimal Volume { get; }
    public decimal OI { get; }

    public Candle(DateTime dateTime, decimal open, decimal high, decimal low,
                  decimal close, decimal volume = 0m, decimal oi = 0m)
    {
        DateTime = dateTime;
        Open = open; High = high; Low = low; Close = close;
        Volume = volume; OI = oi;
    }
}