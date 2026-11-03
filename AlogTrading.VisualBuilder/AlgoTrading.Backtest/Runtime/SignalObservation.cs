namespace AlgoTrading.Backtest.Runtime;

public sealed record SignalObservation(
    DateTime TimestampUtc,
    DateTime AvailableAtUtc,
    bool Eligibility,
    bool Entry,
    bool Protection,
    bool Exit,
    bool FullExit,
    bool StopLoss,
    bool Target,
    IReadOnlyList<EvaluationTraceRecord> Traces)
{
    // Existing observers may provide group results without condition evidence.
    public SignalObservation(DateTime TimestampUtc, DateTime AvailableAtUtc,
        bool Eligibility, bool Entry, bool Protection, bool Exit, bool FullExit,
        bool StopLoss, bool Target)
        : this(TimestampUtc, AvailableAtUtc, Eligibility, Entry, Protection, Exit,
            FullExit, StopLoss, Target, Array.Empty<EvaluationTraceRecord>()) { }
}
