using System.Text;
using Stage1Scanner.Core.Indicators;
using Stage1Scanner.Core.Pivots;
using Stage1Scanner.Core.Series;
using Core = Stage1Scanner.Core.Market;
using Rules = Stage1Scanner.Core.Rules;
using AlgoTrading.Models;

namespace AlgoTrading.Backtest.Runtime;

/// <summary>
/// Resolves an IndicatorExpression to an IndicatorSeries, caching by
/// (token, tf, kind, output, params, source) per Section 4.11. Sources must
/// share the parent indicator's timeframe in v1.
/// </summary>
public sealed partial class IndicatorProvider
{
    private readonly TimeframeAccessor _accessor;
    private readonly Dictionary<string, IndicatorSeries> _cache = new(StringComparer.Ordinal);

    private readonly IReadOnlyDictionary<string, decimal> _parameters;
    public IndicatorProvider(TimeframeAccessor accessor, IReadOnlyDictionary<string, decimal>? parameters = null)
    {
        _accessor = accessor;
        _parameters = parameters ?? new Dictionary<string, decimal>();
    }

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
        if (expr.Indicator == IndicatorKind.Count || expr.Source is not null)
            sb.Append('|').Append(System.Text.Json.JsonSerializer.Serialize(expr));
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
        if (expr.Indicator == IndicatorKind.Count) return ComputeCount(expr, tf);
        var aligned = _accessor.Resolve(tf);
        int tfCount = aligned.TfBarCount;

        var kind = expr.Indicator;
        if (kind is IndicatorKind.PivotPointsClassic or IndicatorKind.PivotPointsCamarilla)
            return ComputePivots(kind, aligned);

        var bars = ToCandles(aligned, tf);
        decimal P(string name, decimal fallback) => expr.Params is not null && expr.Params.TryGetValue(name, out var value) ? value : fallback;
        int Period(int fallback = 20) => (int)P("period", fallback);
        NumericSeries Source(Core.IndicatorType type, int period, int period2 = 0, int period3 = 0,
            decimal multiplier = 0m, Rules.IndicatorComponent component = Rules.IndicatorComponent.Value)
        {
            var values = expr.Source is null ? bars.Select(candle => (decimal?)candle.Close).ToArray() : BuildSourceSeries(expr.Source, tf, aligned, tfCount);
            var spec = new Rules.IndicatorSpec(type, Period1: period, Period2: period2, Period3: period3,
                Decimal1: multiplier, Component: component);
            return new NumericSeries(bars, values, "Builder source").ApplySourceIndicator(spec, spec.ToString());
        }
        IndicatorSeries Single(NumericSeries line) => Result(["Value"], line.Values);
        switch (kind)
        {
            case IndicatorKind.Sma: return Single(Source(Core.IndicatorType.SMA, Period()));
            case IndicatorKind.Ema: return Single(Source(Core.IndicatorType.EMA, Period()));
            case IndicatorKind.Dema: return Single(Source(Core.IndicatorType.DEMA, Period()));
            case IndicatorKind.Tema: return Single(Source(Core.IndicatorType.TEMA, Period()));
            case IndicatorKind.Wma: return Single(Source(Core.IndicatorType.WMA, Period()));
            case IndicatorKind.Hma: return Single(Source(Core.IndicatorType.HMA, Period()));
            case IndicatorKind.Rsi: return Single(Source(Core.IndicatorType.RSI, Period(14)));
            case IndicatorKind.Roc: return Single(Source(Core.IndicatorType.ROC, Period(12)));
            case IndicatorKind.Momentum: return Single(Source(Core.IndicatorType.Momentum, Period(10)));
            case IndicatorKind.Macd:
                return Result(["Macd", "Signal", "Histogram"],
                    Source(Core.IndicatorType.MACD, (int)P("fast", 12), (int)P("slow", 26), (int)P("signal", 9)).Values,
                    Source(Core.IndicatorType.MACD, (int)P("fast", 12), (int)P("slow", 26), (int)P("signal", 9), component: Rules.IndicatorComponent.Signal).Values,
                    Source(Core.IndicatorType.MACD, (int)P("fast", 12), (int)P("slow", 26), (int)P("signal", 9), component: Rules.IndicatorComponent.Histogram).Values);
            case IndicatorKind.BollingerBands:
                return Result(["Upper", "Middle", "Lower"],
                    Source(Core.IndicatorType.BollingerBands, Period(), multiplier: P("stdDev", 2), component: Rules.IndicatorComponent.Upper).Values,
                    Source(Core.IndicatorType.BollingerBands, Period(), multiplier: P("stdDev", 2), component: Rules.IndicatorComponent.Basis).Values,
                    Source(Core.IndicatorType.BollingerBands, Period(), multiplier: P("stdDev", 2), component: Rules.IndicatorComponent.Lower).Values);
            case IndicatorKind.Trix:
                // Preserve the existing signal parameter using Stage1's EMA over Stage1's TRIX.
                var raw = Source(Core.IndicatorType.TRIX, Period(15));
                var signal = new Rules.IndicatorSpec(Core.IndicatorType.EMA, Period1: (int)P("signal", 9));
                return Single(raw.ApplySourceIndicator(signal, "Builder TRIX signal"));
            case IndicatorKind.Atr: return Single(bars.ATR(Period(14)));
            case IndicatorKind.Adx:
                var adx = bars.ADX(Period(14));
                return Result(["Adx", "PlusDi", "MinusDi"], adx.Adx.Values, adx.PlusDi.Values, adx.MinusDi.Values);
            case IndicatorKind.Supertrend:
                var st = bars.SuperTrend(Period(10), P("multiplier", 3));
                return Result(["Value", "Direction", "TurnedBullish", "TurnedBearish"], 
                    st.Value.Values,
                    st.Direction.Select(value => value.HasValue ? (decimal?)(int)value.Value : null).ToArray(),
                    st.TurnedBullish.Select(b => (decimal?)(b ? 1m : 0m)).ToArray(),
                    st.TurnedBearish.Select(b => (decimal?)(b ? 1m : 0m)).ToArray());
            case IndicatorKind.Vwap: return Single(bars.VWAP());
            case IndicatorKind.Obv: return Single(bars.OBV());
            case IndicatorKind.Adl: return Single(bars.ADL());
            case IndicatorKind.Stochastic:
                var stochastic = bars.Stochastic((int)P("kPeriod", 14), (int)P("dPeriod", 3));
                return Result(["K", "D"], stochastic.K.Values, stochastic.D.Values);
            case IndicatorKind.Cci: return Single(bars.CCI(Period()));
            case IndicatorKind.VolumeSma: return Single(bars.VolumeSMA(Period()));
            case IndicatorKind.KeltnerChannel:
                var kc = bars.Keltner(Period(), (int)P("atrPeriod", 10), P("multiplier", 2));
                return Result(["Upper", "Middle", "Lower"], kc.Upper.Values, kc.Basis.Values, kc.Lower.Values);
            case IndicatorKind.DonchianChannel:
                var dc = bars.Donchian(Period());
                return Result(["Upper", "Middle", "Lower"], dc.Upper.Values, dc.Middle.Values, dc.Lower.Values);
            case IndicatorKind.Mfi: return Single(bars.MFI(Period(14)));
            case IndicatorKind.HighLow52W:
                return Result(["High52", "Low52"], bars.High52W(Period(250)).Values, bars.Low52W(Period(250)).Values);
            default: throw new ArgumentOutOfRangeException(nameof(expr), kind, "Unsupported indicator kind.");
        }
    }

    private static Core.Candles ToCandles(AlignedSeries aligned, Timeframe tf)
    {
        var timeframe = tf.Unit switch
        {
            TimeframeUnit.m => Core.Timeframe.Minutes(tf.Count),
            TimeframeUnit.H => Core.Timeframe.Hours(tf.Count),
            TimeframeUnit.D => Core.Timeframe.Day,
            TimeframeUnit.W => Core.Timeframe.Week,
            TimeframeUnit.M => Core.Timeframe.Month,
            _ => throw new ArgumentOutOfRangeException(nameof(tf))
        };
        var items = new Core.Candle[aligned.TfBarCount];
        var times = aligned.Bars.DateTime.Span;
        var open = aligned.GetFieldSpan("Open");
        var high = aligned.GetFieldSpan("High");
        var low = aligned.GetFieldSpan("Low");
        var close = aligned.GetFieldSpan("Close");
        var volume = aligned.GetFieldSpan("Volume");
        for (int i = 0; i < items.Length; i++)
        {
            var time = new DateTimeOffset(DateTime.SpecifyKind(times[i], DateTimeKind.Utc)).ToOffset(TimeSpan.FromMinutes(330));
            if (i > 0 && time <= items[i - 1].OpenTime)
                throw new ArgumentException("Indicator timestamps must be unique and increasing.");
            var end = new DateTimeOffset(DateTime.SpecifyKind(TimeframeAccessor.BarEnd(aligned.Bars, i), DateTimeKind.Utc))
                .ToOffset(TimeSpan.FromMinutes(330));
            items[i] = new Core.Candle(time, end, open[i], high[i], low[i], close[i], volume[i], aligned.Bars.OI.Span[i]);
        }
        return new Core.Candles(timeframe, items);
    }

    private static IndicatorSeries Result(string[] names, params decimal?[][] outputs)
    {
        int firstValid = outputs[0].Length;
        for (int i = 0; i < firstValid; i++)
            if (outputs.Any(output => output[i].HasValue)) { firstValid = i; break; }
        return new IndicatorSeries { OutputNames = names, Outputs = outputs, FirstValidIndex = firstValid };
    }

    private IndicatorSeries ComputePivots(IndicatorKind kind, AlignedSeries target)
    {
        // Use the target's requested timeframe (D, W, or M) instead of hardcoding "D".
        var tf = target.Timeframe;
        var bars = ToCandles(target, tf);
        
        string[] names;
        decimal?[][] outputs;
        
        if (kind == IndicatorKind.PivotPointsCamarilla)
        {
            var cam = bars.Camarilla();
            names = ["R4", "R3", "R2", "R1", "S1", "S2", "S3", "S4"];
            outputs = [cam.R4.Values, cam.R3.Values, cam.R2.Values, cam.R1.Values,
                cam.S1.Values, cam.S2.Values, cam.S3.Values, cam.S4.Values];
        }
        else
        {
            // Output "P" instead of "PP" to match Stage1Scanner.Core.PivotLevel and the UI.
            // Include R4, R5, S4, S5.
            names = ["P", "R1", "R2", "R3", "R4", "R5", "S1", "S2", "S3", "S4", "S5"];
            outputs = names.Select(_ => new decimal?[bars.Count]).ToArray();
            for (int i = 1; i < bars.Count; i++)
            {
                var previous = bars[i - 1];
                var p = PivotHelpers.Traditional(previous.High, previous.Low, previous.Close);
                decimal[] values = [p.P, p.R1, p.R2, p.R3, p.R4, p.R5, p.S1, p.S2, p.S3, p.S4, p.S5];
                for (int k = 0; k < names.Length; k++) outputs[k][i] = values[k];
            }
        }
        
        int firstValid = bars.Count;
        for (int i = 0; i < firstValid; i++)
            if (outputs[0][i].HasValue) { firstValid = i; break; }
            
        return new IndicatorSeries
        {
            OutputNames = names,
            Outputs = outputs,
            FirstValidIndex = firstValid
        };
    }

    private decimal?[] BuildSourceSeries(ValueExpression? src, Timeframe tf,
                                        AlignedSeries aligned, int tfCount)
    {
        if (src is null)
        {
            // None-source indicator: no source series needed. Empty array.
            return Array.Empty<decimal?>();
        }

        if (src is FieldExpression f)
        {
            var srcTf = f.Timeframe;
            if (srcTf.HasValue && !srcTf.Value.Equals(tf))
                throw new NotSupportedException(
                    "v1: indicator source must share the parent indicator's timeframe.");

            var span = aligned.GetFieldSpan(f.Field.ToString());
            var result = new decimal?[tfCount];
            int offset = f.Offset;
            for (int i = 0; i < tfCount; i++)
            {
                int idx = i - offset;
                result[i] = idx < 0 ? null : span[idx];
            }
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
            var result = new decimal?[tfCount];
            for (int i = 0; i < tfCount; i++)
            {
                int idx = i - offset;
                if (idx < inner.FirstValidIndex || idx < 0)
                {
                    result[i] = null;
                    continue;
                }
                var v = innerSpan[idx];
                result[i] = v;
            }
            return result;
        }

        throw new NotSupportedException(
            "Indicator source must be FieldExpression or IndicatorExpression (v1).");
    }
}