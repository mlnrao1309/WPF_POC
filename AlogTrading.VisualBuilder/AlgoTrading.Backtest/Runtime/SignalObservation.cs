namespace AlgoTrading.Backtest.Runtime;

public sealed record SignalObservation(DateTime TimestampUtc, DateTime AvailableAtUtc,
    bool Eligibility, bool Entry, bool Protection, bool Exit, bool FullExit,
    bool StopLoss, bool Target);
