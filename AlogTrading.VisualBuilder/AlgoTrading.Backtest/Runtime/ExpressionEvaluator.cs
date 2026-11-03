using AlgoTrading.Models;

namespace AlgoTrading.Backtest.Runtime;

public sealed class ExpressionEvaluator
{
    private readonly TimeframeAccessor _accessor;
    private readonly IndicatorProvider _indicators;
    private readonly IReadOnlyDictionary<string, decimal> _parameters;
    private readonly IReadOnlyDictionary<string, ReferenceValue> _references;

    public ExpressionEvaluator(
        TimeframeAccessor accessor,
        IndicatorProvider indicators,
        IReadOnlyDictionary<string, decimal> parameters,
        IReadOnlyDictionary<string, ReferenceValue> references)
    {
        _accessor = accessor;
        _indicators = indicators;
        _parameters = parameters;
        _references = references;
    }

    public bool TryGetReferenceInfo(string id, out ReferenceValue value)
        => _references.TryGetValue(id, out value!);

    public bool TryEvaluate(ValueExpression expr, Timeframe defaultTf, int baseIndex, int extraOffset, out decimal value, out DateTimeOffset? availableAt)
    {
        availableAt = null;
        switch (expr)
        {
            case FieldExpression fe:
                return TryField(fe, defaultTf, baseIndex, extraOffset, out value, out availableAt);

            case NumberExpression ne:
                value = ne.Value;
                return true;

            case ParameterExpression pe:
                if (pe.Name is not null && _parameters.TryGetValue(pe.Name, out var pv))
                {
                    value = pv;
                    return true;
                }
                value = 0m;
                return false;

            case IndicatorExpression ie:
                return TryIndicator(ie, defaultTf, baseIndex, extraOffset, out value, out availableAt);

            case ReferenceExpression re:
                if (_references.TryGetValue(re.Id, out var refVal))
                {
                    availableAt = refVal.AvailableAtUtc ?? new DateTimeOffset(
                        refVal.Candle.DateTime.Kind == DateTimeKind.Utc ? refVal.Candle.DateTime
                            : DateTime.SpecifyKind(refVal.Candle.DateTime.AddMinutes(-330), DateTimeKind.Utc));
                    if (re.Field == "Value")
                    {
                        if (refVal.CapturedValue.HasValue) { value = refVal.CapturedValue.Value; return true; }
                        value = 0m; return false;
                    }
                    if (re.Field == "AgeInBars")
                    {
                        value = baseIndex - refVal.CapturedBarIndex;
                        return true;
                    }
                    var bar = refVal.Candle;
                    value = re.Field switch
                    {
                        "High" => bar.High,
                        "Low" => bar.Low,
                        "Open" => bar.Open,
                        "Close" => bar.Close,
                        "Volume" => bar.Volume,
                        _ => 0m
                    };
                    return true;
                }
                value = 0m;
                return false;

            case CalculationExpression ce:
                return TryCalculation(ce, defaultTf, baseIndex, extraOffset, out value, out availableAt);

            default:
                value = 0m;
                return false;
        }
    }

    private bool TryField(FieldExpression fe, Timeframe defaultTf, int baseIndex, int extraOffset, out decimal value, out DateTimeOffset? availableAt)
    {
        var tf = fe.Timeframe ?? defaultTf;
        var aligned = _accessor.Resolve(tf);
        int j = aligned.BaseToTfIndex[baseIndex] - fe.Offset - extraOffset;
        if (j < 0) { value = 0m; availableAt = null; return false; }
        var span = aligned.GetFieldSpan(fe.Field.ToString());
        value = span[j];
        var barEnd = TimeframeAccessor.BarEnd(aligned.Bars, j);
        availableAt = new DateTimeOffset(barEnd, TimeSpan.Zero);
        return true;
    }

    private bool TryIndicator(IndicatorExpression ie, Timeframe defaultTf, int baseIndex, int extraOffset, out decimal value, out DateTimeOffset? availableAt)
    {
        var tf = ie.Timeframe ?? defaultTf;
        var aligned = _accessor.Resolve(tf);
        int j = aligned.BaseToTfIndex[baseIndex] - ie.Offset - extraOffset;
        if (j < 0) { value = 0m; availableAt = null; return false; }
        var series = _indicators.Resolve(ie, defaultTf);
        if (j < series.FirstValidIndex) { value = 0m; availableAt = null; return false; }
        var outName = ie.Output ?? series.OutputNames[0];
        int oi = series.IndexOfOutput(outName);
        if (oi < 0) { value = 0m; availableAt = null; return false; }
        var v = series.Outputs[oi][j];
        if (v is null) { value = 0m; availableAt = null; return false; }
        value = v.Value;
        var barEnd = TimeframeAccessor.BarEnd(aligned.Bars, j);
        availableAt = new DateTimeOffset(barEnd, TimeSpan.Zero);
        return true;
    }

    private bool TryCalculation(CalculationExpression ce, Timeframe defaultTf, int baseIndex, int extraOffset, out decimal value, out DateTimeOffset? availableAt)
    {
        availableAt = null;
        if (ce.Left is null || ce.Right is null) { value = 0m; return false; }
        if (!TryEvaluate(ce.Left, defaultTf, baseIndex, extraOffset, out var l, out var lTime)) { value = 0m; return false; }
        if (!TryEvaluate(ce.Right, defaultTf, baseIndex, extraOffset, out var r, out var rTime)) { value = 0m; return false; }
        availableAt = lTime.HasValue && rTime.HasValue
            ? (lTime > rTime ? lTime : rTime)
            : (lTime ?? rTime);
        switch (ce.Op)
        {
            case CalculationOp.Add: value = l + r; return true;
            case CalculationOp.Subtract: value = l - r; return true;
            case CalculationOp.Multiply: value = l * r; return true;
            case CalculationOp.Divide: if (r == 0m) { value = 0m; return false; } value = l / r; return true;
            case CalculationOp.Modulo: if (r == 0m) { value = 0m; return false; } value = l % r; return true;
            case CalculationOp.Min: value = Math.Min(l, r); return true;
            case CalculationOp.Max: value = Math.Max(l, r); return true;
            default: value = 0m; return false;
        }
    }
}