namespace AlgoTrading.Services;

internal sealed class PollingIntervalSchedule
{
    public required string Interval { get; init; }
    public required TimeSpan Duration { get; init; }
    public required TimeSpan SafetyDelay { get; init; }
    public DateTime CandleClose { get; set; }
    public DateTime NextDueTime => CandleClose + SafetyDelay;

    public void Advance() => CandleClose = CandleClose.Add(Duration);
}
