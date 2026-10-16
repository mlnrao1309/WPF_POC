using Stage1Scanner.Core.Market;

namespace Stage1Scanner.Core.Rules;

public sealed class EvaluationContext
{
    public EvaluationContext(
        Instruments instruments,
        DateTimeOffset evaluationTime,
        bool timeFrameInheritable = false,
        Timeframe? rootTimeFrame = null)
    {
        Instruments = instruments ?? throw new ArgumentNullException(nameof(instruments));
        EvaluationTime = evaluationTime;
        TimeFrameInheritable = timeFrameInheritable;
        RootTimeFrame = rootTimeFrame;
    }

    public Instruments Instruments { get; }
    public DateTimeOffset EvaluationTime { get; }
    public bool TimeFrameInheritable { get; }
    public Timeframe? RootTimeFrame { get; }

    /// <summary>
    /// Optional per-evaluation trace sink. Null when tracing is disabled.
    /// Rule / RuleSet / Aggregate emit through this hook so tracing does not
    /// couple the rule engine to the Scanner.
    /// </summary>
    public Action<string>? Trace { get; init; }

    /// <summary>
    /// Emits a trace line if tracing is enabled. Null-safe and cheap when
    /// <see cref="Trace"/> is null.
    /// </summary>
    public void TraceLine(string message) => Trace?.Invoke(message);

    public EvaluationContext At(DateTimeOffset evaluationTime)
        => new(
            Instruments,
            evaluationTime,
            TimeFrameInheritable,
            RootTimeFrame)
        {
            Trace = Trace
        };

    public bool TryResolveTimeframe(Timeframe? requested, out Timeframe timeframe)
    {
        if (requested.HasValue)
        {
            timeframe = requested.Value;
            return true;
        }

        if (TimeFrameInheritable && RootTimeFrame.HasValue)
        {
            timeframe = RootTimeFrame.Value;
            return true;
        }

        timeframe = default;
        return false;
    }

    public int ResolveClosedIndex(Timeframe timeframe, DateTimeOffset at)
    {
        if (!Instruments.TryGetCandles(timeframe, out var candles))
        {
            return -1;
        }

        for (var i = candles.Count - 1; i >= 0; i--)
        {
            if (candles[i].CloseTime <= at)
            {
                return i;
            }
        }

        return -1;
    }
}