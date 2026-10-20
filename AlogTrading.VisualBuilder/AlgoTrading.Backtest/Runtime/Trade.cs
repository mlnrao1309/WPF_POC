using AlgoTrading.Models;

namespace AlgoTrading.Backtest.Runtime;

public sealed class Trade
{
    public int TradeId { get; init; }
    public Direction Direction { get; init; }
    public DateTime EntryTime { get; init; }
    public DateTime ExitTime { get; init; }
    public decimal EntryPrice { get; init; }
    public decimal ExitPrice { get; init; }
    public int Quantity { get; init; }

    public decimal GrossPnL { get; init; }
    public decimal Brokerage { get; init; }
    public decimal STT { get; init; }
    public decimal ExchangeTxn { get; init; }
    public decimal SEBI { get; init; }
    public decimal StampDuty { get; init; }
    public decimal GST { get; init; }
    public decimal TotalCosts { get; init; }
    public decimal NetPnL { get; init; }

    public decimal MAE { get; init; }
    public decimal MFE { get; init; }

    public int BarsHeld { get; init; }
    public decimal DaysHeld { get; init; }
    public ExitReason ExitReason { get; init; }
}