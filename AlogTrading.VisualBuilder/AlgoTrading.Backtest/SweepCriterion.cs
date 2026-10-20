namespace AlgoTrading.Backtest;

/// <summary>
/// Selection criterion for parameter sweeps (Section 5.15).
/// </summary>
public enum SweepCriterion
{
    Sharpe,
    ProfitFactor,
    NetPnL,
    Calmar
}