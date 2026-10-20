namespace AlgoTrading.Backtest.Cli.Bridge;

/// <summary>Backtest execution mode. Mirrors Section 5/6 of the spec.</summary>
public enum RunMode
{
    Single,
    Sweep,
    WalkForward
}