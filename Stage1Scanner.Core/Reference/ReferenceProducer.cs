using Stage1Scanner.Core.Market;
using Stage1Scanner.Core.Rules;

namespace Stage1Scanner.Core.ScannerModel;

/// <summary>
/// Declares a stateful reference. When Trigger first evaluates True on a
/// closed bar of Timeframe, that bar's OHLC is captured and exposed via
/// ReferenceOperand. Persists until ClearOn fires True (if provided),
/// at which point it can be re-armed on a subsequent Trigger.
/// </summary>
public sealed class ReferenceProducer
{
    public ReferenceProducer(
        string id,
        IRuleExpression trigger,
        Timeframe timeframe,
        IRuleExpression? clearOn = null)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException(
                "Reference id is required.", nameof(id));
        }

        Id = id;
        Trigger = trigger ?? throw new ArgumentNullException(nameof(trigger));
        Timeframe = timeframe;
        ClearOn = clearOn;
    }

    public string Id { get; }
    public IRuleExpression Trigger { get; }
    public Timeframe Timeframe { get; }
    public IRuleExpression? ClearOn { get; }

    /// <summary>
    /// Called once per instrument per Scanner run, before RuleSets evaluate.
    /// </summary>
    public void Prepare(Instruments instruments, EvaluationContext context)
    {
        var exists = instruments.References.TryGet(Id, out _);

        if (exists)
        {
            if (ClearOn is null)
            {
                return;
            }

            var clear = ClearOn.Evaluate(context);
            if (clear.Truth != RuleTruth.True)
            {
                return;
            }

            instruments.References.Clear(Id);
            // Fall through: allows re-capture on this same bar if Trigger fires.
        }

        var trigger = Trigger.Evaluate(context);
        if (trigger.Truth != RuleTruth.True)
        {
            return;
        }

        if (!instruments.TryGetCandles(Timeframe, out var candles))
        {
            return;
        }

        var idx = context.ResolveClosedIndex(Timeframe, context.EvaluationTime);
        if (idx < 0)
        {
            return;
        }

        var bar = candles[idx];

        instruments.References.Set(new Reference(
            Id,
            Timeframe,
            bar.OpenTime,
            bar.CloseTime,
            bar.Open,
            bar.High,
            bar.Low,
            bar.Close,
            context.EvaluationTime));
    }
}