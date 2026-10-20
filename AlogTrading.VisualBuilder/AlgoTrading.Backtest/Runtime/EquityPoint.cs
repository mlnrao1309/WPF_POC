namespace AlgoTrading.Backtest.Runtime;

public readonly struct EquityPoint
{
    public DateTime DateTime { get; init; }
    public decimal Equity { get; init; }
}