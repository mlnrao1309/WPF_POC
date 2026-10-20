using System.Text;
using AlgoTrading.Indicators;
using AlgoTrading.Models;

namespace AlgoTrading.Backtest.Runtime;

/// <summary>
/// Resolves an IndicatorExpression to an IndicatorSeries, caching by
/// (token, tf, kind, output, params, source) per Section 4.11. Sources must
/// share the parent indicator's timeframe in v1.
/// </summary>
public sealed class IndicatorProvider
{
    private readonly TimeframeAccessor _accessor;
    private readonly Dictionary<string, IndicatorSeries> _cache = new(StringComparer.Ordinal);

    public IndicatorProvider(TimeframeAccessor accessor) { _accessor = accessor; }

    public IndicatorSeries Resolve(IndicatorExpression expr, Timeframe defaultTf)
    {
        ArgumentNullException.ThrowIfNull(expr);
        var tf = expr.Timeframe ?? defaultTf;
        var key = BuildKey(tf, expr);
        if (_cache.TryGetValue(key, out var cached)) return cached;

        var series = Compute(expr, tf, defaultTf);
        _cache[key] = series;
        return series;
    }

    private string BuildKey(Timeframe tf, IndicatorExpression expr)
    {
        var sb = new StringBuilder(96);
        sb.Append(_accessor.BaseSeries.InstrumentToken.ToString())
          .Append('|').Append(tf.ToString())
          .Append('|').Append(expr.Indicator.ToString())
          .Append('|').Append(expr.Output ?? "")
          .Append('|').Append(ParamsKey(expr.Params))
          .Append('|').Append(SourceKey(expr.Source, tf));
        return sb.ToString();
    }

    private static string ParamsKey(IReadOnlyDictionary<string, decimal>? p)
    {
        if (p is null || p.Count == 0) return "";
        var keys = p.Keys.ToList();
        keys.Sort(StringComparer.Ordinal);
        var sb = new StringBuilder();
        foreach (var k in keys) sb.Append(k).Append('=').Append(p[k]).Append(';');
        return sb.ToString();
    }

    private static string SourceKey(ValueExpression? src, Timeframe tf)
    {
        if (src is null) return "";
        var sb = new StringBuilder();
        switch (src)
        {
            case FieldExpression f:
                sb.Append("F:").Append(f.Field.ToString()).Append('@')
                  .Append((f.Timeframe ?? tf).ToString()).Append('+').Append(f.Offset);
                break;
            case IndicatorExpression i:
                sb.Append("I:")
                  .Append((i.Timeframe ?? tf).ToString()).Append('|')
                  .Append(i.Indicator.ToString()).Append('|')
                  .Append(i.Output ?? "").Append('|')
                  .Append(ParamsKey(i.Params)).Append('|')
                  .Append(SourceKey(i.Source, tf)).Append('+').Append(i.Offset);
                break;
            case NumberExpression n:
                sb.Append("N:").Append(n.Value);
                break;
            case ParameterExpression p:
                sb.Append("P:").Append(p.Name ?? "");
                break;
            case CalculationExpression c:
                sb.Append("C:").Append(c.Op.ToString())
                  .Append('(').Append(SourceKey(c.Left, tf))
                  .Append(',').Append(SourceKey(c.Right, tf)).Append(')');
                break;
        }
        return sb.ToString();
    }

    private IndicatorSeries Compute(IndicatorExpression expr, Timeframe tf, Timeframe defaultTf)
    {
        var aligned = _accessor.Resolve(tf);
        int tfCount = aligned.TfBarCount;

        var kind = expr.Indicator;
        if (!IndicatorRegistry.TryGet(kind, out var descriptor))
            throw new InvalidOperationException("Indicator not registered: " + kind);

        if (kind is IndicatorKind.PivotPointsClassic or IndicatorKind.PivotPointsCamarilla)
            return ComputeDailyPivots(kind, descriptor, aligned);

        var sourceArr = BuildSourceSeries(expr.Source, tf, aligned, tfCount, out var sourceFirstValid);

        var paramValues = new decimal[descriptor.Parameters.Count];
        for (int i = 0; i < descriptor.Parameters.Count; i++)
        {
            var p = descriptor.Parameters[i];
            if (expr.Params is not null && expr.Params.TryGetValue(p.Name, out var v))
                paramValues[i] = v;
            else
                paramValues[i] = p.Default;
        }

        int declaredOutCount = descriptor.Outputs.Count;
        if (declaredOutCount < 1) declaredOutCount = 1;

        // EvaluateIndicator takes 8 output slots; empty spans for unused ones.
        var slots = new decimal?[8][];
        for (int k = 0; k < 8; k++)
            slots[k] = k < declaredOutCount ? new decimal?[tfCount] : Array.Empty<decimal?>();

        var high = aligned.GetFieldSpan("High");
        var low = aligned.GetFieldSpan("Low");
        var close = aligned.GetFieldSpan("Close");
        var volume = aligned.GetFieldSpan("Volume");
        var dt = aligned.Bars.DateTime.Span;

        IndicatorDispatcher.EvaluateIndicator(
            kind,
            sourceArr.AsSpan(),
            high,
            low,
            close,
            volume,
            dt,
            paramValues,
            slots[0].AsSpan(),
            slots[1].AsSpan(),
            slots[2].AsSpan(),
            slots[3].AsSpan(),
            slots[4].AsSpan(),
            slots[5].AsSpan(),
            slots[6].AsSpan(),
            slots[7].AsSpan(),
            out int actualOutCount);

        var list = new decimal?[actualOutCount][];
        for (int k = 0; k < actualOutCount; k++) list[k] = slots[k];

        // v1 nesting rule: outer indicator is only valid where its source was
        // valid for at least its own warmup. Approximate as
        // sourceFirstValid + WarmupBarsAtDefault; null out everything before.
        int firstValid = descriptor.WarmupBarsAtDefault;
        if (sourceFirstValid > 0)
            firstValid += sourceFirstValid;
        if (firstValid > tfCount) firstValid = tfCount;

        return new IndicatorSeries
        {
            OutputNames = descriptor.Outputs.ToArray(),
            Outputs = list,
            FirstValidIndex = firstValid
        };
    }

    private IndicatorSeries ComputeDailyPivots(
        IndicatorKind kind, IndicatorDescriptor descriptor, AlignedSeries target)
    {
        var daily = _accessor.Resolve(Timeframe.Parse("D"));
        int dailyCount = daily.TfBarCount;
        int outputCount = descriptor.Outputs.Count;

        var dailyOutputs = new decimal?[8][];
        for (int k = 0; k < 8; k++)
            dailyOutputs[k] = k < outputCount
                ? new decimal?[dailyCount]
                : Array.Empty<decimal?>();

        IndicatorDispatcher.EvaluateIndicator(
            kind,
            ReadOnlySpan<decimal>.Empty,
            daily.GetFieldSpan("High"),
            daily.GetFieldSpan("Low"),
            daily.GetFieldSpan("Close"),
            daily.GetFieldSpan("Volume"),
            daily.Bars.DateTime.Span,
            ReadOnlySpan<decimal>.Empty,
            dailyOutputs[0].AsSpan(),
            dailyOutputs[1].AsSpan(),
            dailyOutputs[2].AsSpan(),
            dailyOutputs[3].AsSpan(),
            dailyOutputs[4].AsSpan(),
            dailyOutputs[5].AsSpan(),
            dailyOutputs[6].AsSpan(),
            dailyOutputs[7].AsSpan(),
            out int actualOutputCount);

        int targetCount = target.TfBarCount;
        var projected = new decimal?[actualOutputCount][];
        for (int k = 0; k < actualOutputCount; k++)
            projected[k] = new decimal?[targetCount];

        var dailyDates = daily.Bars.DateTime.Span;
        var targetDates = target.Bars.DateTime.Span;
        int dailyIndex = 0;
        int firstValid = targetCount;

        for (int i = 0; i < targetCount; i++)
        {
            int targetDay = Vwap.TradingDayKey(targetDates[i]);
            while (dailyIndex < dailyCount &&
                   Vwap.TradingDayKey(dailyDates[dailyIndex]) < targetDay)
            {
                dailyIndex++;
            }

            if (dailyIndex >= dailyCount ||
                Vwap.TradingDayKey(dailyDates[dailyIndex]) != targetDay)
            {
                continue;
            }

            for (int k = 0; k < actualOutputCount; k++)
                projected[k][i] = dailyOutputs[k][dailyIndex];

            if (firstValid == targetCount && projected[0][i].HasValue)
                firstValid = i;
        }

        return new IndicatorSeries
        {
            OutputNames = descriptor.Outputs.ToArray(),
            Outputs = projected,
            FirstValidIndex = firstValid
        };
    }

    private decimal[] BuildSourceSeries(ValueExpression? src, Timeframe tf,
                                        AlignedSeries aligned, int tfCount,
                                        out int firstValid)
    {
        firstValid = 0;
        if (src is null)
        {
            // None-source indicator: no source series needed. Empty array.
            return Array.Empty<decimal>();
        }

        if (src is FieldExpression f)
        {
            var srcTf = f.Timeframe;
            if (srcTf.HasValue && !srcTf.Value.Equals(tf))
                throw new NotSupportedException(
                    "v1: indicator source must share the parent indicator's timeframe.");

            var span = aligned.GetFieldSpan(f.Field.ToString());
            var result = new decimal[tfCount];
            int offset = f.Offset;
            for (int i = 0; i < tfCount; i++)
            {
                int idx = i - offset;
                result[i] = idx < 0 ? 0m : span[idx];
            }
            firstValid = offset;
            return result;
        }

        if (src is IndicatorExpression innerExpr)
        {
            var srcTf = innerExpr.Timeframe;
            if (srcTf.HasValue && !srcTf.Value.Equals(tf))
                throw new NotSupportedException(
                    "v1: indicator source must share the parent indicator's timeframe.");

            var inner = Resolve(innerExpr, tf);
            var outName = innerExpr.Output ?? inner.OutputNames[0];
            int oi = inner.IndexOfOutput(outName);
            if (oi < 0) throw new InvalidOperationException("Unknown output: " + outName);

            var innerSpan = inner.Outputs[oi];
            int offset = innerExpr.Offset;
            var result = new decimal[tfCount];
            for (int i = 0; i < tfCount; i++)
            {
                int idx = i - offset;
                if (idx < inner.FirstValidIndex || idx < 0)
                {
                    result[i] = 0m;
                    continue;
                }
                var v = innerSpan[idx];
                result[i] = v ?? 0m;
            }
            firstValid = inner.FirstValidIndex + offset;
            return result;
        }

        throw new NotSupportedException(
            "Indicator source must be FieldExpression or IndicatorExpression (v1).");
    }
}
