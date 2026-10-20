using System;
using AlgoTrading.Models;

namespace AlgoTrading.Backtest;

public readonly record struct Trade
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

internal sealed class TradeRecord
{
    public int TradeId;
    public Direction Direction;
    public int EntryBarIndex;
    public DateTime EntryTime;
    public decimal EntryPrice;
    public int Quantity;
    public decimal StopLossPrice;
    public decimal TargetPrice;
    public bool HasStopLoss;
    public bool HasTarget;
    public decimal MAE;
    public decimal MFE;

    public void UpdateExcursions(Direction dir, decimal high, decimal low)
    {
        if (dir == Direction.Long)
        {
            decimal a = EntryPrice - low; if (a > MAE) MAE = a;
            decimal f = high - EntryPrice; if (f > MFE) MFE = f;
        }
        else
        {
            decimal a = high - EntryPrice; if (a > MAE) MAE = a;
            decimal f = EntryPrice - low;  if (f > MFE) MFE = f;
        }
    }
}

public enum PositionState { Flat, Entered, Exited }

public sealed class RejectedEntry
{
    public int BarIndex { get; init; }
    public DateTime DateTime { get; init; }
    public string Reason { get; init; } = "";
    public decimal RequiredMargin { get; init; }
    public decimal AvailableCapital { get; init; }
}