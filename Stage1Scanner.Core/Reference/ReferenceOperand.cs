using Stage1Scanner.Core.Market;

namespace Stage1Scanner.Core.Rules;

/// <summary>
/// Reads a value from a captured Reference. Returns Unknown if the
/// reference has not been produced yet on this instrument.
/// </summary>
/// <summary>
/// Reads a value from a captured Reference. Returns Unknown if the
/// reference has not been produced yet on this instrument.
/// </summary>
public sealed record ReferenceOperand(string Id, ReferenceField Field) : Operand
{
    public override OperandValue Resolve(
        EvaluationContext context,
        int relativeOffset = 0,
        DateTimeOffset? referenceTime = null)
    {
        if (!context.Instruments.References.TryGet(Id, out var reference))
        {
            return OperandValue.Unknown;
        }

        var name = $"Reference[{Id}].{Field}";

        return Field switch
        {
            ReferenceField.Open => OperandValue.From(reference.Open, reference.BarCloseTime, name, reference.Timeframe),
            ReferenceField.High => OperandValue.From(reference.High, reference.BarCloseTime, name, reference.Timeframe),
            ReferenceField.Low => OperandValue.From(reference.Low, reference.BarCloseTime, name, reference.Timeframe),
            ReferenceField.Close => OperandValue.From(reference.Close, reference.BarCloseTime, name, reference.Timeframe),
            ReferenceField.BarOpenTime => OperandValue.From(reference.BarOpenTime.ToUnixTimeSeconds(), null, name, reference.Timeframe),
            ReferenceField.BarCloseTime => OperandValue.From(reference.BarCloseTime.ToUnixTimeSeconds(), null, name, reference.Timeframe),
            ReferenceField.AgeInBars => ResolveAge(context, reference, name),

            // NEW: Resolve the custom calculated value
            ReferenceField.Value => reference.CapturedValue.HasValue
                ? OperandValue.From(reference.CapturedValue.Value, reference.BarCloseTime, name, reference.Timeframe)
                : OperandValue.Unknown,

            _ => OperandValue.Unknown
        };
    }

    private static OperandValue ResolveAge(
        EvaluationContext context,
        Reference reference,
        string name)
    {
        if (!context.Instruments.TryGetCandles(reference.Timeframe, out var candles))
        {
            return OperandValue.Unknown;
        }

        var currentIdx = context.ResolveClosedIndex(reference.Timeframe, context.EvaluationTime);

        if (currentIdx < 0)
        {
            return OperandValue.Unknown;
        }

        var ageInBars = 0;

        for (var i = currentIdx; i >= 0; i--)
        {
            if (candles[i].CloseTime <= reference.BarCloseTime)
            {
                return OperandValue.From(ageInBars, context.EvaluationTime, name, reference.Timeframe);
            }

            ageInBars++;
        }

        return OperandValue.Unknown;
    }
}