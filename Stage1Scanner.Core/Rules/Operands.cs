using System.Globalization;
using Stage1Scanner.Core.Indicators;
using Stage1Scanner.Core.Market;
using Stage1Scanner.Core.Pivots;
using Stage1Scanner.Core.Series;

namespace Stage1Scanner.Core.Rules;

public readonly record struct OperandValue(
    bool HasValue,
    decimal Value,
    DateTimeOffset? SourceTime = null,
    string? Name = null,
    Timeframe? TimeFrame = null,
    int Offset = 0)
{
    public static OperandValue Unknown => new(false, default);

    public static OperandValue From(
        decimal value,
        DateTimeOffset? sourceTime = null,
        string? name = null,
        Timeframe? timeFrame = null,
        int offset = 0)
        => new(true, value, sourceTime, name, timeFrame, offset);
}

public abstract record Operand
{
    public abstract OperandValue Resolve(
        EvaluationContext context,
        int relativeOffset = 0,
        DateTimeOffset? referenceTime = null);

    public static Operand FromNumber(decimal value)
        => new NumberOperand(value);

    public static Operand FromField(
        SourceField field,
        Timeframe? timeframe = null,
        int offset = 0)
        => new CandleFieldOperand(field, timeframe, offset);

    public static Operand FromIndicator(IndicatorSpec spec, int offset = 0)
        => new IndicatorOperand(spec, offset);

    public static Operand FromPivot(
        PivotLevel level,
        Timeframe pivotTimeframe,
        int offset = 0)
        => new PivotOperand(level, pivotTimeframe, offset);

    public static Operand FromCriticalLevel(string id)
        => new CriticalLevelOperand(id);

    public static Operand FromZoneLow(string id)
        => new CriticalZoneOperand(id, High: false);

    public static Operand FromZoneHigh(string id)
        => new CriticalZoneOperand(id, High: true);

    public static Operand FromAnchorLow(string structureId)
        => new AnchorOperand(structureId, High: false);

    public static Operand FromAnchorHigh(string structureId)
        => new AnchorOperand(structureId, High: true);
    public static Operand FromReference(string id, ReferenceField field)
    => new ReferenceOperand(id, field);
}

public sealed record NumberOperand(decimal Number) : Operand
{
    public override OperandValue Resolve(
        EvaluationContext context,
        int relativeOffset = 0,
        DateTimeOffset? referenceTime = null)
        => OperandValue.From(
            Number,
            name: Number.ToString(CultureInfo.InvariantCulture));
}

public sealed record CandleFieldOperand(
    SourceField Field,
    Timeframe? Timeframe = null,
    int Offset = 0) : Operand
{
    public override OperandValue Resolve(
        EvaluationContext context,
        int relativeOffset = 0,
        DateTimeOffset? referenceTime = null)
    {
        if (Offset < 0 || relativeOffset < 0 ||
            !context.TryResolveTimeframe(Timeframe, out var timeframe) ||
            !context.Instruments.TryGetCandles(timeframe, out var candles))
        {
            return OperandValue.Unknown;
        }

        var aligned = context.ResolveClosedIndex(timeframe, context.EvaluationTime);
        var index = aligned - Offset - relativeOffset;

        if (index < 0 || index >= candles.Count)
        {
            return OperandValue.Unknown;
        }

        var candle = candles[index];
        var totalOffset = Offset + relativeOffset;
        var name = Field.ToString();

        return Field switch
        {
            SourceField.Open => OperandValue.From(
                candle.Open, candle.CloseTime, name, timeframe, totalOffset),
            SourceField.High => OperandValue.From(
                candle.High, candle.CloseTime, name, timeframe, totalOffset),
            SourceField.Low => OperandValue.From(
                candle.Low, candle.CloseTime, name, timeframe, totalOffset),
            SourceField.Close => OperandValue.From(
                candle.Close, candle.CloseTime, name, timeframe, totalOffset),
            SourceField.Volume => OperandValue.From(
                candle.Volume, candle.CloseTime, name, timeframe, totalOffset),
            SourceField.OpenInterest when candle.OpenInterest.HasValue
                => OperandValue.From(
                    candle.OpenInterest.Value, candle.CloseTime, name, timeframe, totalOffset),
            _ => OperandValue.Unknown
        };
    }
}

public sealed record IndicatorOperand(
    IndicatorSpec Spec,
    int Offset = 0) : Operand
{
    public override OperandValue Resolve(
        EvaluationContext context,
        int relativeOffset = 0,
        DateTimeOffset? referenceTime = null)
    {
        if (Offset < 0 || relativeOffset < 0 ||
            !context.TryResolveTimeframe(Spec.Timeframe, out var timeframe) ||
            !context.Instruments.TryGetCandles(timeframe, out var candles))
        {
            return OperandValue.Unknown;
        }

        var index = context.ResolveClosedIndex(timeframe, context.EvaluationTime)
                    - Offset - relativeOffset;

        if (index < 0 || index >= candles.Count)
        {
            return OperandValue.Unknown;
        }

        var series = ResolveSeries(candles, Spec);

        if (!series.TryGetValue(index, out var value))
        {
            return OperandValue.Unknown;
        }

        var totalOffset = Offset + relativeOffset;
        var name = BuildIndicatorName(Spec);

        return OperandValue.From(
            value,
            candles[index].CloseTime,
            name,
            timeframe,
            totalOffset);
    }

    private static string BuildIndicatorName(IndicatorSpec spec)
    {
        if (spec.Period1 <= 0)
        {
            var simple = spec.Kind.ToString();
            return spec.Component != IndicatorComponent.Value
                ? $"{simple}.{spec.Component}"
                : simple;
        }

        var name = $"{spec.Kind}({spec.Period1}";
        if (spec.Period2 > 0) name += $",{spec.Period2}";
        if (spec.Period3 > 0) name += $",{spec.Period3}";
        if (spec.Decimal1 != 0m) name += $",{spec.Decimal1}";
        name += ")";

        if (spec.Component != IndicatorComponent.Value)
        {
            name += $".{spec.Component}";
        }

        return name;
    }

    private static NumericSeries ResolveSeries(Candles candles, IndicatorSpec spec)
    {
        if (spec.Input is not null)
        {
            var source = ResolveInputSeries(candles, spec.Input);
            return source.ApplySourceIndicator(spec, $"NESTED|{BuildSpecKey(spec)}");
        }

        return spec.Kind switch
        {
            IndicatorType.SMA => candles.SMA(spec.Period1, spec.Source),
            IndicatorType.EMA => candles.EMA(spec.Period1, spec.Source),
            IndicatorType.DEMA => candles.DEMA(spec.Period1, spec.Source),
            IndicatorType.TEMA => candles.TEMA(spec.Period1, spec.Source),
            IndicatorType.WMA => candles.WMA(spec.Period1, spec.Source),
            IndicatorType.HMA => candles.HMA(spec.Period1, spec.Source),
            IndicatorType.RSI => ResolveRsi(candles, spec),
            IndicatorType.ATR => candles.ATR(spec.Period1),
            IndicatorType.VWAP => candles.VWAP(),
            IndicatorType.OBV => candles.OBV(),
            IndicatorType.CCI => candles.CCI(spec.Period1),
            IndicatorType.VolumeSMA => candles.VolumeSMA(spec.Period1),
            IndicatorType.High52W => candles.High52W(spec.Period1 > 0 ? spec.Period1 : 252),
            IndicatorType.Low52W => candles.Low52W(spec.Period1 > 0 ? spec.Period1 : 252),
            IndicatorType.ROC => candles.ROC(spec.Period1, spec.Source),
            IndicatorType.Momentum => candles.Momentum(spec.Period1, spec.Source),
            IndicatorType.TRIX => candles.TRIX(spec.Period1, spec.Source),
            IndicatorType.MACD => ResolveMacd(candles, spec),
            IndicatorType.BollingerBands => ResolveBb(candles, spec),
            IndicatorType.ADX => ResolveAdx(candles, spec),
            IndicatorType.Supertrend => ResolveSupertrend(candles, spec),
            IndicatorType.Stochastic => ResolveStochastic(candles, spec),
            IndicatorType.Keltner => ResolveKeltner(candles, spec),
            IndicatorType.Donchian => ResolveDonchian(candles, spec),
            _ => throw new NotSupportedException(
                $"Indicator {spec.Kind} is not a scalar rule operand.")
        };
    }

    private static NumericSeries ResolveInputSeries(Candles candles, Operand input)
        => input switch
        {
            CandleFieldOperand field => ResolveFieldSeries(candles, field),
            IndicatorOperand indicator => ShiftSeries(candles,
                ResolveNestedIndicator(candles, indicator), indicator.Offset,
                $"{BuildSpecKey(indicator.Spec)}|{indicator.Offset}"),
            _ => throw new InvalidOperationException(
                "Indicator input must be a candle field or another indicator.")
        };

    private static NumericSeries ResolveNestedIndicator(Candles candles, IndicatorOperand indicator)
    {
        ValidateInputTimeframe(candles, indicator.Spec.Timeframe);
        if (indicator.Offset < 0) throw new InvalidOperationException("Nested indicator offset cannot be negative.");
        return ResolveSeries(candles, indicator.Spec);
    }

    private static NumericSeries ResolveFieldSeries(Candles candles, CandleFieldOperand field)
    {
        ValidateInputTimeframe(candles, field.Timeframe);
        if (field.Offset < 0) throw new InvalidOperationException("Nested field offset cannot be negative.");
        var key = $"NESTED-FIELD|{field.Field}|{field.Offset}";
        return IndicatorCache.GetOrCreate(candles, key, () =>
        {
            var values = new decimal?[candles.Count];
            for (var i = field.Offset; i < candles.Count; i++)
            {
                var candle = candles[i - field.Offset];
                values[i] = field.Field switch
                {
                    SourceField.Open => candle.Open,
                    SourceField.High => candle.High,
                    SourceField.Low => candle.Low,
                    SourceField.Close => candle.Close,
                    SourceField.Volume => candle.Volume,
                    SourceField.OpenInterest => candle.OpenInterest,
                    _ => throw new ArgumentOutOfRangeException(nameof(field.Field))
                };
            }
            return new NumericSeries(candles, values, $"{field.Field}[{field.Offset}]");
        });
    }

    private static NumericSeries ShiftSeries(Candles candles, NumericSeries source, int offset, string sourceKey)
    {
        if (offset == 0) return source;
        return IndicatorCache.GetOrCreate(candles, $"NESTED-SHIFT|{sourceKey}", () =>
        {
            var values = new decimal?[source.Count];
            if (offset < source.Count)
                source.Values.AsSpan(0, source.Count - offset).CopyTo(values.AsSpan(offset));
            return new NumericSeries(candles, values, $"{source.Name}[{offset}]");
        });
    }

    private static void ValidateInputTimeframe(Candles candles, Timeframe? timeframe)
    {
        if (timeframe.HasValue && timeframe.Value != candles.Timeframe)
            throw new InvalidOperationException("Nested indicator inputs must use the outer indicator timeframe.");
    }

    private static string BuildSpecKey(IndicatorSpec spec)
        => $"{spec.Kind}|{spec.Timeframe}|{spec.Source}|{spec.Period1}|{spec.Period2}|{spec.Period3}|" +
           $"{spec.Decimal1.ToString(CultureInfo.InvariantCulture)}|{spec.Smoothing}|{spec.Component}|" +
           (spec.Input switch
           {
               CandleFieldOperand field => $"FIELD:{field.Field}:{field.Timeframe}:{field.Offset}",
               IndicatorOperand indicator => $"IND:{indicator.Offset}:{BuildSpecKey(indicator.Spec)}",
               null => "DEFAULT",
               _ => spec.Input.GetType().Name
           });

    private static NumericSeries ResolveRsi(Candles candles, IndicatorSpec spec)
    {
        var wantsDivergence = spec.Component == IndicatorComponent.RsiDivergence;

        var result = candles.RSI(
            spec.Period1 > 0 ? spec.Period1 : 14,
            spec.Smoothing,
            spec.Period2 > 0 ? spec.Period2 : 14,
            calculateDivergence: wantsDivergence,
            source: spec.Source);

        return spec.Component switch
        {
            IndicatorComponent.RsiSmooth when result.Smooth is not null
                => result.Smooth,

            IndicatorComponent.RsiSmooth
                => throw new InvalidOperationException(
                    "RsiSmooth component requires Smoothing to be set on the IndicatorSpec."),

            IndicatorComponent.RsiDivergence
                => DivergenceToSeries(candles, result.Value, result.Divergence),

            _ => result.Value
        };
    }

    private static NumericSeries DivergenceToSeries(
        Candles candles,
        NumericSeries rsi,
        RsiDivergenceType?[] divergence)
    {
        var output = new decimal?[divergence.Length];

        for (var i = 0; i < output.Length; i++)
        {
            if (!rsi.Values[i].HasValue)
            {
                output[i] = null;
                continue;
            }

            output[i] = divergence[i] switch
            {
                RsiDivergenceType.Bullish => 1m,
                RsiDivergenceType.Bearish => -1m,
                _ => 0m
            };
        }

        return new NumericSeries(candles, output, "RSI.Divergence");
    }

    private static NumericSeries ResolveSupertrend(Candles candles, IndicatorSpec spec)
    {
        var result = candles.SuperTrend(
            spec.Period1 > 0 ? spec.Period1 : 10,
            spec.Decimal1 == 0m ? 3m : spec.Decimal1);

        return spec.Component switch
        {
            IndicatorComponent.Direction => DirectionToSeries(candles, result),
            IndicatorComponent.TurnedBullish => BoolToSeries(candles, result.TurnedBullish, "Supertrend.TurnedBullish"),
            IndicatorComponent.TurnedBearish => BoolToSeries(candles, result.TurnedBearish, "Supertrend.TurnedBearish"),
            _ => result.Value
        };
    }

    private static NumericSeries DirectionToSeries(Candles candles, SuperTrendSeries st)
    {
        var output = new decimal?[st.Direction.Length];

        for (var i = 0; i < output.Length; i++)
        {
            output[i] = st.Direction[i] switch
            {
                SuperTrendDirection.Bullish => 1m,
                SuperTrendDirection.Bearish => -1m,
                _ => null
            };
        }

        return new NumericSeries(candles, output, "Supertrend.Direction");
    }

    private static NumericSeries BoolToSeries(
        Candles candles,
        bool[] source,
        string name)
    {
        var output = new decimal?[source.Length];

        for (var i = 0; i < output.Length; i++)
        {
            output[i] = source[i] ? 1m : 0m;
        }

        return new NumericSeries(candles, output, name);
    }

    private static NumericSeries ResolveMacd(Candles candles, IndicatorSpec spec)
    {
        var result = candles.MACD(
            spec.Period1 > 0 ? spec.Period1 : 12,
            spec.Period2 > 0 ? spec.Period2 : 26,
            spec.Period3 > 0 ? spec.Period3 : 9,
            spec.Source);

        return spec.Component switch
        {
            IndicatorComponent.Signal => result.Signal,
            IndicatorComponent.Histogram => result.Histogram,
            _ => result.Macd
        };
    }

    private static NumericSeries ResolveBb(Candles candles, IndicatorSpec spec)
    {
        var result = candles.BollingerBands(
            spec.Period1 > 0 ? spec.Period1 : 20,
            spec.Decimal1 == 0m ? 2m : spec.Decimal1,
            spec.Source);

        return spec.Component switch
        {
            IndicatorComponent.Upper => result.Upper,
            IndicatorComponent.Lower => result.Lower,
            _ => result.Basis
        };
    }

    private static NumericSeries ResolveAdx(Candles candles, IndicatorSpec spec)
    {
        var result = candles.ADX(spec.Period1 > 0 ? spec.Period1 : 14);

        return spec.Component switch
        {
            IndicatorComponent.PlusDi => result.PlusDi,
            IndicatorComponent.MinusDi => result.MinusDi,
            _ => result.Adx
        };
    }

    private static NumericSeries ResolveStochastic(Candles candles, IndicatorSpec spec)
    {
        var result = candles.Stochastic(
            spec.Period1 > 0 ? spec.Period1 : 14,
            spec.Period2 > 0 ? spec.Period2 : 3);

        return spec.Component == IndicatorComponent.D
            ? result.D
            : result.K;
    }

    private static NumericSeries ResolveKeltner(Candles candles, IndicatorSpec spec)
    {
        var result = candles.Keltner(
            spec.Period1 > 0 ? spec.Period1 : 20,
            spec.Period2 > 0 ? spec.Period2 : 10,
            spec.Decimal1 == 0m ? 2m : spec.Decimal1);

        return spec.Component switch
        {
            IndicatorComponent.Upper => result.Upper,
            IndicatorComponent.Lower => result.Lower,
            _ => result.Basis
        };
    }

    private static NumericSeries ResolveDonchian(Candles candles, IndicatorSpec spec)
    {
        var result = candles.Donchian(spec.Period1 > 0 ? spec.Period1 : 20);

        return spec.Component switch
        {
            IndicatorComponent.Upper => result.Upper,
            IndicatorComponent.Lower => result.Lower,
            _ => result.Middle
        };
    }
}

public sealed record PivotOperand(
    PivotLevel Level,
    Timeframe PivotTimeframe,
    int Offset = 0) : Operand
{
    public override OperandValue Resolve(
        EvaluationContext context,
        int relativeOffset = 0,
        DateTimeOffset? referenceTime = null)
    {
        if (Offset < 0 || relativeOffset < 0)
        {
            return OperandValue.Unknown;
        }

        var effectiveReference = referenceTime ?? context.EvaluationTime;
        var pivotOffset = Offset + (referenceTime.HasValue ? 0 : relativeOffset);

        return PivotResolver.TryResolve(
            context.Instruments,
            PivotTimeframe,
            Level,
            pivotOffset,
            effectiveReference,
            out var value)
                ? OperandValue.From(
                    value,
                    effectiveReference,
                    name: Level.ToString(),
                    timeFrame: PivotTimeframe,
                    offset: pivotOffset)
                : OperandValue.Unknown;
    }
}

public sealed record CriticalLevelOperand(string Id) : Operand
{
    public override OperandValue Resolve(
        EvaluationContext context,
        int relativeOffset = 0,
        DateTimeOffset? referenceTime = null)
        => context.Instruments.Structures.TryGetLevel(Id, out var level)
            ? OperandValue.From(
                level.Price,
                level.SourceCloseTime,
                name: $"CriticalLevel[{Id}]",
                timeFrame: level.Timeframe)
            : OperandValue.Unknown;
}

public sealed record CriticalZoneOperand(string Id, bool High) : Operand
{
    public override OperandValue Resolve(
        EvaluationContext context,
        int relativeOffset = 0,
        DateTimeOffset? referenceTime = null)
        => context.Instruments.Structures.TryGetZone(Id, out var zone)
            ? OperandValue.From(
                High ? zone.High : zone.Low,
                zone.SourceCloseTime,
                name: $"CriticalZone[{Id}].{(High ? "High" : "Low")}",
                timeFrame: zone.Timeframe)
            : OperandValue.Unknown;
}

public sealed record AnchorOperand(string StructureId, bool High) : Operand
{
    public override OperandValue Resolve(
        EvaluationContext context,
        int relativeOffset = 0,
        DateTimeOffset? referenceTime = null)
        => context.Instruments.Structures.TryGetAnchor(StructureId, out var anchor)
            ? OperandValue.From(
                High ? anchor.High : anchor.Low,
                anchor.CandleCloseTime,
                name: $"Anchor[{StructureId}].{(High ? "High" : "Low")}")
            : OperandValue.Unknown;
}
