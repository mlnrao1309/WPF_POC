using AlgoTrading.Models;

namespace AlgoTrading.Backtest.Contracts;

public readonly struct TradeExecutionContext
{
    public required decimal EntryPrice { get; init; }
    public required decimal ExitPrice { get; init; }
    public required int Quantity { get; init; }
    public required Direction Direction { get; init; }
    public required ExecutionMode Mode { get; init; }
}

public sealed class CostBreakdown
{
    public decimal Brokerage { get; init; }
    public decimal STT { get; init; }
    public decimal ExchangeTxn { get; init; }
    public decimal SEBI { get; init; }
    public decimal StampDuty { get; init; }
    public decimal GST { get; init; }
    public decimal Total => Brokerage + STT + ExchangeTxn + SEBI + StampDuty + GST;
}

public interface ITransactionCostModel
{
    CostBreakdown Compute(TradeExecutionContext ctx);
}