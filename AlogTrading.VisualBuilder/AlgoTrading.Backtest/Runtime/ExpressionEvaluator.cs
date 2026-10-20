using AlgoTrading.Models;

namespace AlgoTrading.Backtest.Runtime;

/// <summary>
/// Resolves a ValueExpression at a given base bar index. extraOffset is added
/// to the expression's own Offset where the expression has one; used by
/// crossover semantics (Â§2.5) where the "previous" value is the same
/// expression at offset+1. Returns false when the expression has no value at
/// that bar (start of data or warmup).
/// </summary>
public sealed class ExpressionEvaluator
{
    private readonly TimeframeAccessor _accessor;
    private readonly IndicatorProvider _indicators;
    private readonly IReadOnlyDictionary<string, decimal> _parameters;

    public ExpressionEvaluator(TimeframeAccessor accessor,
                               IndicatorProvider indicators,
                               IReadOnlyDictionary<string, decimal> parameters)
    {
        _accessor = accessor;
        _indicators = indicators;
        _parameters = parameters;
    }

    public bool TryEvaluate(ValueExpression expr, Timeframe defaultTf,
                            int baseIndex, int extraOffset, out decimal value)
    {
        switch (expr)
        {
            case FieldExpression fe:
                return TryField(fe, defaultTf, baseIndex, extraOffset, out value);

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
                return TryIndicator(ie, defaultTf, baseIndex, extraOffset, out value);

            case CalculationExpression ce:
                return TryCalculation(ce, defaultTf, baseIndex, extraOffset, out value);

            default:
                value = 0m;
                return false;
        }
    }

    private bool TryField(FieldExpression fe, Timeframe defaultTf,
                          int baseIndex, int extraOffset, out decimal value)
    {
        var tf = fe.Timeframe ?? defaultTf;
        var aligned = _accessor.Resolve(tf);

        int j = aligned.BaseToTfIndex[baseIndex] - fe.Offset - extraOffset;
        if (j < 0) { value = 0m; return false; }

        var span = aligned.GetFieldSpan(fe.Field.ToString());
        value = span[j];
        return true;
    }

    private bool TryIndicator(IndicatorExpression ie, Timeframe defaultTf,
                              int baseIndex, int extraOffset, out decimal value)
    {
        var tf = ie.Timeframe ?? defaultTf;
        var aligned = _accessor.Resolve(tf);

        int j = aligned.BaseToTfIndex[baseIndex] - ie.Offset - extraOffset;
        if (j < 0) { value = 0m; return false; }

        var series = _indicators.Resolve(ie, defaultTf);
        if (j < series.FirstValidIndex) { value = 0m; return false; }

        var outName = ie.Output ?? series.OutputNames[0];
        int oi = series.IndexOfOutput(outName);
        if (oi < 0) { value = 0m; return false; }

        var v = series.Outputs[oi][j];
        if (v is null) { value = 0m; return false; }
        value = v.Value;
        return true;
    }

    private bool TryCalculation(CalculationExpression ce, Timeframe defaultTf,
                                int baseIndex, int extraOffset, out decimal value)
    {
        if (ce.Left is null || ce.Right is null) { value = 0m; return false; }
        if (!TryEvaluate(ce.Left, defaultTf, baseIndex, extraOffset, out var l)) { value = 0m; return false; }
        if (!TryEvaluate(ce.Right, defaultTf, baseIndex, extraOffset, out var r)) { value = 0m; return false; }

        switch (ce.Op)
        {
            case CalculationOp.Add:      value = l + r; return true;
            case CalculationOp.Subtract: value = l - r; return true;
            case CalculationOp.Multiply: value = l * r; return true;
            case CalculationOp.Divide:
                if (r == 0m) { value = 0m; return false; }
                value = l / r; return true;
            case CalculationOp.Modulo:
                if (r == 0m) { value = 0m; return false; }
                value = l % r; return true;
            case CalculationOp.Min:      value = Math.Min(l, r); return true;
            case CalculationOp.Max:      value = Math.Max(l, r); return true;
            default:                     value = 0m; return false;
        }
    }
}