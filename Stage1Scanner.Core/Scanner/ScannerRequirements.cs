using Stage1Scanner.Core.Market;
using Stage1Scanner.Core.Pivots;
using Stage1Scanner.Core.Rules;

namespace Stage1Scanner.Core.ScannerModel;

public sealed class ScannerRequirements
{
    public HashSet<Timeframe> Timeframes { get; } = [];

    /// <summary>
    /// Deepest explicit operand offset written in Rules for each timeframe.
    /// Internal history needed by crossing/aggregates is intentionally not
    /// folded into this value.
    /// </summary>
    public Dictionary<Timeframe, int> MaximumOffset { get; } = [];

    /// <summary>
    /// Minimum number of bars to load per timeframe after considering
    /// indicator warm-up, explicit offsets and rule/aggregate history.
    /// </summary>
    public Dictionary<Timeframe, int> MinimumBarsRequired { get; } = [];

    /// <summary>
    /// Reference ids referenced by any ReferenceOperand in the Scanner's
    /// rules. Every id in this set must have a matching ReferenceProducer
    /// registered on the Scanner; orphan ids fail validation.
    /// </summary>
    public HashSet<string> ReferencedReferenceIds { get; } =
        new(StringComparer.OrdinalIgnoreCase);
}

public static class ScannerRequirementAnalyzer
{
    public static ScannerRequirements GetRequirements(this Scanner scanner)
    {
        ArgumentNullException.ThrowIfNull(scanner);
        scanner.ValidateConfiguration();

        var requirements = new ScannerRequirements();
        var declaredReferenceIds = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

        Collect(scanner, scanner.BullishConditions, requirements);
        Collect(scanner, scanner.BearishConditions, requirements);
        Collect(scanner, scanner.RangeBoundConditions, requirements);
        Collect(scanner, scanner.TransitionConditions, requirements);

        foreach (var producer in scanner.ReferenceProducers)
        {
            if (!declaredReferenceIds.Add(producer.Id))
            {
                throw new InvalidOperationException(
                    $"Duplicate ReferenceProducer id '{producer.Id}' on " +
                    $"Scanner '{scanner.Name}'. Every ReferenceProducer must " +
                    "have a unique id.");
            }

            // The producer needs at least one closed bar on its own timeframe
            // to capture anything. Additional history for trigger and clear
            // expressions is collected below.
            Register(
                requirements,
                producer.Timeframe,
                explicitOffset: 0,
                minimumBars: 1);

            Collect(scanner, producer.Trigger, requirements);

            if (producer.ClearOn is not null)
            {
                Collect(scanner, producer.ClearOn, requirements);
            }
        }

        ValidateReferencedReferenceIds(
            scanner, requirements, declaredReferenceIds);

        foreach (var helper in scanner.CriticalLevelHelpers)
        {
            helper.AddRequirements(requirements);
        }

        foreach (var timeframe in requirements.Timeframes)
        {
            requirements.MaximumOffset.TryAdd(timeframe, 0);
            requirements.MinimumBarsRequired.TryAdd(timeframe, 1);
        }

        return requirements;
    }

    /// <summary>
    /// Every ReferenceOperand id used in a rule must have a ReferenceProducer
    /// with the same id declared on the Scanner. Fails fast at configuration
    /// time; without this check an orphan operand silently resolves Unknown.
    /// </summary>
    private static void ValidateReferencedReferenceIds(
        Scanner scanner,
        ScannerRequirements requirements,
        HashSet<string> declaredIds)
    {
        foreach (var id in requirements.ReferencedReferenceIds)
        {
            if (!declaredIds.Contains(id))
            {
                throw new InvalidOperationException(
                    $"Rule references reference id '{id}', but no " +
                    $"ReferenceProducer with that id is registered on " +
                    $"Scanner '{scanner.Name}'.");
            }
        }
    }

    private static void Collect(
        Scanner scanner,
        IRuleExpression expression,
        ScannerRequirements requirements,
        int additionalHistory = 0)
    {
        switch (expression)
        {
            case Rule rule:
                CollectRule(scanner, rule, requirements, additionalHistory);
                return;

            case BetweenRule between:
                CollectOperand(scanner, between.Value, requirements, additionalHistory);
                CollectOperand(scanner, between.Lower, requirements, additionalHistory);
                CollectOperand(scanner, between.Upper, requirements, additionalHistory);
                return;

            case RuleSet ruleSet:
                foreach (var child in ruleSet.Expressions)
                {
                    Collect(scanner, child, requirements, additionalHistory);
                }
                return;

            case AggregateRule aggregate:
                CollectAggregate(scanner, aggregate, requirements, additionalHistory);
                return;

            default:
                throw new NotSupportedException(
                    $"Unsupported rule expression: {expression.GetType().Name}.");
        }
    }

    private static void CollectRule(
        Scanner scanner,
        Rule rule,
        ScannerRequirements requirements,
        int additionalHistory)
    {
        var operatorHistory = rule.Operator is
            RuleOperator.CrossedAbove or
            RuleOperator.CrossedBelow
                ? 1
                : 0;

        var requiredHistory = checked(additionalHistory + operatorHistory);

        CollectOperand(scanner, rule.Left, requirements, requiredHistory);
        CollectOperand(scanner, rule.Right, requirements, requiredHistory);
    }

    private static void CollectAggregate(
        Scanner scanner,
        AggregateRule aggregate,
        ScannerRequirements requirements,
        int additionalHistory)
    {
        var timeframe = ResolveTimeframe(
            scanner,
            aggregate.Timeframe,
            "AggregateRule");

        var aggregateEvaluations = aggregate.Function switch
        {
            AggregateFunction.Count =>
                aggregate.Lookback is > 0
                    ? aggregate.Lookback.Value
                    : throw new InvalidOperationException(
                        "Count requires a positive Lookback."),

            AggregateFunction.CountStreak =>
                AggregateHistory.RequiredStreakEvaluations(
                    aggregate.Compare,
                    aggregate.Threshold),

            _ => throw new ArgumentOutOfRangeException()
        };

        Register(
            requirements,
            timeframe,
            explicitOffset: 0,
            minimumBars: aggregateEvaluations);

        Collect(
            scanner,
            aggregate.Expression,
            requirements,
            checked(additionalHistory + aggregateEvaluations - 1));
    }

    private static void CollectOperand(
        Scanner scanner,
        Operand operand,
        ScannerRequirements requirements,
        int additionalHistory,
        Timeframe? inheritedTimeframe = null)
    {
        switch (operand)
        {
            case NumberOperand:
            case CriticalLevelOperand:
            case CriticalZoneOperand:
            case AnchorOperand:
                return;

            case ReferenceOperand reference:
                // The producer for this id is expected to run during
                // evaluation. Record the id so we can validate that a
                // ReferenceProducer was actually registered.
                requirements.ReferencedReferenceIds.Add(reference.Id);
                return;

            case CandleFieldOperand field:
            {
                ValidateOffset(field.Offset);
                var timeframe = ResolveTimeframe(
                    scanner,
                    field.Timeframe ?? inheritedTimeframe,
                    nameof(CandleFieldOperand));

                Register(
                    requirements,
                    timeframe,
                    field.Offset,
                    checked(field.Offset + additionalHistory + 1));
                return;
            }

            case IndicatorOperand indicator:
            {
                ValidateOffset(indicator.Offset);
                var timeframe = ResolveTimeframe(
                    scanner,
                    indicator.Spec.Timeframe ?? inheritedTimeframe,
                    nameof(IndicatorOperand));

                var warmup = IndicatorRequirements.GetMinimumBars(
                    indicator.Spec);

                Register(
                    requirements,
                    timeframe,
                    indicator.Offset,
                    checked(warmup + indicator.Offset + additionalHistory));
                if (indicator.Spec.Input is not null)
                {
                    var inputTimeframe = indicator.Spec.Input switch
                    {
                        CandleFieldOperand fieldInput => fieldInput.Timeframe,
                        IndicatorOperand indicatorInput => indicatorInput.Spec.Timeframe,
                        _ => throw new InvalidOperationException(
                            "Indicator input must be a candle field or another indicator.")
                    };
                    if (inputTimeframe.HasValue && inputTimeframe.Value != timeframe)
                        throw new InvalidOperationException(
                            "Nested indicator inputs must use the outer indicator timeframe.");
                    CollectOperand(scanner, indicator.Spec.Input, requirements,
                        checked(additionalHistory + indicator.Offset + warmup - 1), timeframe);
                }
                return;
            }

            case PivotOperand pivot:
            {
                ValidateOffset(pivot.Offset);

                if (!PivotResolver.IsTraditionalPivotTimeframe(
                        pivot.PivotTimeframe))
                {
                    throw new InvalidOperationException(
                        $"Traditional Pivot timeframe must be D, W or M. " +
                        $"Received {pivot.PivotTimeframe}.");
                }

                // Traditional pivot set effective for a session is calculated
                // from the previous completed D/W/M source candle.
                Register(
                    requirements,
                    pivot.PivotTimeframe,
                    pivot.Offset,
                    checked(pivot.Offset + additionalHistory + 2));
                return;
            }

            default:
                throw new NotSupportedException(
                    $"Unsupported operand: {operand.GetType().Name}.");
        }
    }

    private static Timeframe ResolveTimeframe(
        Scanner scanner,
        Timeframe? requested,
        string owner)
    {
        if (requested.HasValue)
        {
            return requested.Value;
        }

        if (scanner.TimeFrameInheritable && scanner.RootTimeFrame.HasValue)
        {
            return scanner.RootTimeFrame.Value;
        }

        throw new InvalidOperationException(
            $"{owner} has no timeframe. Specify a timeframe or enable " +
            "TimeFrameInheritable with RootTimeFrame.");
    }

    private static void Register(
        ScannerRequirements requirements,
        Timeframe timeframe,
        int explicitOffset,
        int minimumBars)
    {
        requirements.Timeframes.Add(timeframe);

        if (!requirements.MaximumOffset.TryGetValue(
                timeframe,
                out var currentOffset) ||
            explicitOffset > currentOffset)
        {
            requirements.MaximumOffset[timeframe] = explicitOffset;
        }

        if (!requirements.MinimumBarsRequired.TryGetValue(
                timeframe,
                out var currentMinimum) ||
            minimumBars > currentMinimum)
        {
            requirements.MinimumBarsRequired[timeframe] = minimumBars;
        }
    }

    private static void ValidateOffset(int offset)
    {
        if (offset < 0)
        {
            throw new InvalidOperationException(
                "Rule operand Offset cannot be negative.");
        }
    }
}

public static class IndicatorRequirements
{
    /// <summary>
    /// Returns enough bars for the current indicator implementations to
    /// produce the first non-null scalar value. This is deliberately a data
    /// loading requirement, not a trading-quality warm-up recommendation.
    /// </summary>
    public static int GetMinimumBars(IndicatorSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);

        static int P(int value, int fallback)
            => value > 0 ? value : fallback;

        var p1 = P(spec.Period1, 14);
        var p2 = P(spec.Period2, 0);
        var p3 = P(spec.Period3, 0);

        return spec.Kind switch
        {
            IndicatorType.SMA => p1,
            IndicatorType.EMA => p1,
            IndicatorType.DEMA => checked((2 * p1) - 1),
            IndicatorType.TEMA => checked((3 * p1) - 2),
            IndicatorType.WMA => p1,
            IndicatorType.HMA => checked(p1 + Math.Max(1, (int)Math.Round(Math.Sqrt(p1))) - 1),

            IndicatorType.RSI => checked(
                p1 + 1 +
                (spec.Smoothing.HasValue
                    ? Math.Max(0, P(spec.Period2, 14) - 1)
                    : 0)),

            IndicatorType.MACD => checked(
                P(spec.Period2, 26) + P(spec.Period3, 9) - 1),

            IndicatorType.BollingerBands => P(spec.Period1, 20),
            IndicatorType.ATR => p1,
            IndicatorType.ADX => checked(2 * p1),
            IndicatorType.Supertrend => P(spec.Period1, 10),
            IndicatorType.VWAP => 1,
            IndicatorType.OBV => 1,

            IndicatorType.Stochastic => checked(
                P(spec.Period1, 14) + P(spec.Period2, 3) - 1),

            IndicatorType.CCI => P(spec.Period1, 20),
            IndicatorType.VolumeSMA => P(spec.Period1, 20),
            IndicatorType.High52W => P(spec.Period1, 252),
            IndicatorType.Low52W => P(spec.Period1, 252),
            IndicatorType.ROC => checked(p1 + 1),
            IndicatorType.Momentum => checked(p1 + 1),
            IndicatorType.TRIX => checked(3 * p1),

            IndicatorType.Keltner => Math.Max(
                P(spec.Period1, 20),
                P(spec.Period2, 10)),

            IndicatorType.Donchian => P(spec.Period1, 20),

            _ => throw new NotSupportedException(
                $"Minimum bars are not defined for {spec.Kind}.")
        };
    }
}
