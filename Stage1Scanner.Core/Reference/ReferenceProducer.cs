using Stage1Scanner.Core.Market;
using Stage1Scanner.Core.Rules;

namespace Stage1Scanner.Core.ScannerModel;

public sealed record ReferenceCaptureDef(string Id, Operand Value);

public sealed class ReferenceProducer
{
    public ReferenceProducer(
        string blockName,
        IRuleExpression trigger,
        Timeframe timeframe,
        IReadOnlyList<ReferenceCaptureDef> captures,
        bool overwriteOnTrigger) // <-- NEW
    {
        BlockName = blockName;
        Trigger = trigger ?? throw new ArgumentNullException(nameof(trigger));
        Timeframe = timeframe;
        Captures = captures ?? new List<ReferenceCaptureDef>();
        OverwriteOnTrigger = overwriteOnTrigger;
    }

    public string BlockName { get; }
    public IRuleExpression Trigger { get; }
    public Timeframe Timeframe { get; }
    public IReadOnlyList<ReferenceCaptureDef> Captures { get; }
    public bool OverwriteOnTrigger { get; }

    public void Prepare(Instruments instruments, EvaluationContext context)
    {
        // Check if any of the captures currently exist
        var anyExists = Captures.Any(c => instruments.References.TryGet(c.Id, out _));

        // If they exist, and we are NOT set to overwrite, do nothing.
        if (anyExists && !OverwriteOnTrigger) return;

        var trigger = Trigger.Evaluate(context);
        if (trigger.Truth != RuleTruth.True) return;

        if (!instruments.TryGetCandles(Timeframe, out var candles)) return;

        var idx = context.ResolveClosedIndex(Timeframe, context.EvaluationTime);
        if (idx < 0) return;

        var bar = candles[idx];

        foreach (var capture in Captures)
        {
            var valResult = capture.Value.Resolve(context);
            decimal? capturedValue = valResult.HasValue ? valResult.Value : null;

            instruments.References.Set(new Reference(
                capture.Id,
                Timeframe,
                bar.OpenTime,
                bar.CloseTime,
                bar.Open,
                bar.High,
                bar.Low,
                bar.Close,
                capturedValue,
                context.EvaluationTime));
        }
    }
}