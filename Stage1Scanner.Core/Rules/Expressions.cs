using Stage1Scanner.Core.Market;

namespace Stage1Scanner.Core.Rules;

public interface IRuleExpression
{
    RuleEvaluation Evaluate(EvaluationContext context);
}

/// <summary>
/// Result of evaluating a rule expression. Leaves (Rule, BetweenRule,
/// AggregateRule) carry the resolved operand values via LeftOperand /
/// RightOperand. RuleSet nodes carry their child evaluations in Children
/// so evidence collection can walk the full tree.
/// </summary>
public sealed record RuleEvaluation(
    RuleTruth Truth,
    string Reason,
    decimal? Left = null,
    decimal? Right = null,
    IReadOnlyList<RuleEvaluation>? Children = null,
    OperandValue? LeftOperand = null,
    OperandValue? RightOperand = null);

public sealed class Rule : IRuleExpression
{
    public Rule(
        Operand left,
        RuleOperator @operator,
        Operand right)
    {
        Left = left ?? throw new ArgumentNullException(nameof(left));
        Operator = @operator;
        Right = right ?? throw new ArgumentNullException(nameof(right));
    }

    public Operand Left { get; }
    public RuleOperator Operator { get; }
    public Operand Right { get; }

    public static Rule Create(
        Operand left,
        RuleOperator @operator,
        Operand right)
        => new(left, @operator, right);

    public static Rule Create(
        Operand left,
        RuleOperator @operator,
        decimal number)
        => new(left, @operator, Operand.FromNumber(number));

    public RuleEvaluation Evaluate(EvaluationContext context)
    {
        if (Operator is RuleOperator.CrossedAbove or RuleOperator.CrossedBelow)
        {
            return EvaluateCross(context);
        }

        var (left, right) = ResolvePair(context, relativeOffset: 0);

        if (!left.HasValue || !right.HasValue)
        {
            return new RuleEvaluation(
                RuleTruth.Unknown,
                "RULE_UNKNOWN",
                LeftOperand: left,
                RightOperand: right);
        }

        var truth = Operator switch
        {
            RuleOperator.GreaterThan => left.Value > right.Value,
            RuleOperator.GreaterOrEqual => left.Value >= right.Value,
            RuleOperator.LessThan => left.Value < right.Value,
            RuleOperator.LessOrEqual => left.Value <= right.Value,
            RuleOperator.Equal => left.Value == right.Value,
            RuleOperator.NotEqual => left.Value != right.Value,
            RuleOperator.ClosedAbove => left.Value > right.Value,
            RuleOperator.ClosedBelow => left.Value < right.Value,
            _ => throw new ArgumentOutOfRangeException()
        };

        var reason = Operator switch
        {
            RuleOperator.ClosedAbove when truth => "CLOSED_ABOVE",
            RuleOperator.ClosedBelow when truth => "CLOSED_BELOW",
            _ => truth ? "RULE_MATCH" : "RULE_NOT_MATCH"
        };

        return new RuleEvaluation(
            truth ? RuleTruth.True : RuleTruth.False,
            reason,
            left.Value,
            right.Value,
            LeftOperand: left,
            RightOperand: right);
    }

    private RuleEvaluation EvaluateCross(EvaluationContext context)
    {
        var (nowLeft, nowRight) = ResolvePair(context, relativeOffset: 0);
        var (previousLeft, previousRight) = ResolvePair(context, relativeOffset: 1);

        if (!nowLeft.HasValue || !nowRight.HasValue ||
            !previousLeft.HasValue || !previousRight.HasValue)
        {
            return new RuleEvaluation(
                RuleTruth.Unknown,
                "RULE_UNKNOWN",
                LeftOperand: nowLeft,
                RightOperand: nowRight);
        }

        var crossed = Operator switch
        {
            RuleOperator.CrossedAbove =>
                nowLeft.Value > nowRight.Value &&
                previousLeft.Value <= previousRight.Value,

            RuleOperator.CrossedBelow =>
                nowLeft.Value < nowRight.Value &&
                previousLeft.Value >= previousRight.Value,

            _ => false
        };

        return new RuleEvaluation(
            crossed ? RuleTruth.True : RuleTruth.False,
            crossed
                ? Operator == RuleOperator.CrossedAbove
                    ? "CROSSED_ABOVE"
                    : "CROSSED_BELOW"
                : "RULE_NOT_MATCH",
            nowLeft.Value,
            nowRight.Value,
            LeftOperand: nowLeft,
            RightOperand: nowRight);
    }

    private (OperandValue Left, OperandValue Right) ResolvePair(
        EvaluationContext context,
        int relativeOffset)
    {
        // Pivot resolution must use the session containing the compared
        // candle/indicator bar, not merely the wall-clock evaluation date.
        if (Left is PivotOperand && IsCandleTimed(Right))
        {
            var right = Right.Resolve(context, relativeOffset);
            var left = Left.Resolve(
                context,
                relativeOffset,
                right.SourceTime);
            return (left, right);
        }

        if (Right is PivotOperand && IsCandleTimed(Left))
        {
            var left = Left.Resolve(context, relativeOffset);
            var right = Right.Resolve(
                context,
                relativeOffset,
                left.SourceTime);
            return (left, right);
        }

        return (
            Left.Resolve(context, relativeOffset),
            Right.Resolve(context, relativeOffset));
    }

    private static bool IsCandleTimed(Operand operand)
        => operand is CandleFieldOperand or IndicatorOperand;
}

public sealed class BetweenRule : IRuleExpression
{
    public BetweenRule(
        Operand value,
        Operand lower,
        Operand upper)
    {
        Value = value ?? throw new ArgumentNullException(nameof(value));
        Lower = lower ?? throw new ArgumentNullException(nameof(lower));
        Upper = upper ?? throw new ArgumentNullException(nameof(upper));
    }

    public Operand Value { get; }
    public Operand Lower { get; }
    public Operand Upper { get; }

    public RuleEvaluation Evaluate(EvaluationContext context)
    {
        var value = Value.Resolve(context);
        var referenceTime = value.SourceTime;

        var lower = Lower is PivotOperand
            ? Lower.Resolve(context, referenceTime: referenceTime)
            : Lower.Resolve(context);

        var upper = Upper is PivotOperand
            ? Upper.Resolve(context, referenceTime: referenceTime)
            : Upper.Resolve(context);

        if (!value.HasValue || !lower.HasValue || !upper.HasValue)
        {
            return new RuleEvaluation(
                RuleTruth.Unknown,
                "RULE_UNKNOWN",
                LeftOperand: value,
                RightOperand: lower);
        }

        var low = Math.Min(lower.Value, upper.Value);
        var high = Math.Max(lower.Value, upper.Value);
        var truth = value.Value >= low && value.Value <= high;

        return new RuleEvaluation(
            truth ? RuleTruth.True : RuleTruth.False,
            truth ? "BETWEEN_MATCH" : "RULE_NOT_MATCH",
            value.Value,
            null,
            LeftOperand: value);
    }
}

public sealed class RuleSet : IRuleExpression
{
    public RuleSet(RuleSetOperator @operator = RuleSetOperator.And)
    {
        Operator = @operator;
    }

    public RuleSetOperator Operator { get; set; }
    public IList<IRuleExpression> Expressions { get; } = new List<IRuleExpression>();

    public void Add(IRuleExpression expression)
    {
        ArgumentNullException.ThrowIfNull(expression);
        Expressions.Add(expression);
    }

    public RuleEvaluation Evaluate(EvaluationContext context)
    {
        if (Expressions.Count == 0)
        {
            return new RuleEvaluation(
                RuleTruth.False,
                "RULESET_EMPTY",
                Children: Array.Empty<RuleEvaluation>());
        }

        var results = Expressions
            .Select(x => x.Evaluate(context))
            .ToArray();

        var truth = Operator == RuleSetOperator.And
            ? RuleTruthLogic.And(results.Select(x => x.Truth))
            : RuleTruthLogic.Or(results.Select(x => x.Truth));

        return new RuleEvaluation(
            truth,
            truth switch
            {
                RuleTruth.True => "RULESET_MATCH",
                RuleTruth.False => "RULESET_NOT_MATCH",
                _ => "RULESET_UNKNOWN"
            },
            Children: results);
    }
}

public sealed class AggregateRule : IRuleExpression
{
    public AggregateRule(
        AggregateFunction function,
        IRuleExpression expression,
        Timeframe? timeframe,
        ComparisonOperator compare,
        int threshold,
        int? lookback = null)
    {
        Function = function;
        Expression = expression ?? throw new ArgumentNullException(nameof(expression));
        Timeframe = timeframe;
        Compare = compare;
        Threshold = threshold;
        Lookback = lookback;
    }

    public AggregateFunction Function { get; }
    public IRuleExpression Expression { get; }
    public Timeframe? Timeframe { get; }
    public ComparisonOperator Compare { get; }
    public int Threshold { get; }
    public int? Lookback { get; }

    public RuleEvaluation Evaluate(EvaluationContext context)
    {
        if (!context.TryResolveTimeframe(Timeframe, out var timeframe) ||
            !context.Instruments.TryGetCandles(timeframe, out var candles))
        {
            return new RuleEvaluation(
                RuleTruth.Unknown,
                "RULE_UNKNOWN");
        }

        var currentIndex = context.ResolveClosedIndex(
            timeframe,
            context.EvaluationTime);

        if (currentIndex < 0)
        {
            return new RuleEvaluation(
                RuleTruth.Unknown,
                "RULE_UNKNOWN");
        }

        var value = Function switch
        {
            AggregateFunction.Count =>
                EvaluateCount(context, candles, currentIndex),

            AggregateFunction.CountStreak =>
                EvaluateCountStreak(context, candles, currentIndex),

            _ => AggregateValue.Unknown
        };

        if (!value.HasValue)
        {
            return new RuleEvaluation(
                RuleTruth.Unknown,
                "RULE_UNKNOWN");
        }

        var match = CompareInt(
            value.Value,
            Compare,
            Threshold);

        return new RuleEvaluation(
            match ? RuleTruth.True : RuleTruth.False,
            Function == AggregateFunction.Count
                ? match ? "COUNT_MATCH" : "RULE_NOT_MATCH"
                : match ? "COUNT_STREAK_MATCH" : "RULE_NOT_MATCH",
            value.Value,
            Threshold);
    }

    private AggregateValue EvaluateCount(
        EvaluationContext context,
        Candles candles,
        int currentIndex)
    {
        if (!Lookback.HasValue || Lookback.Value <= 0)
        {
            throw new InvalidOperationException(
                "Count requires a positive Lookback.");
        }

        var start = currentIndex - Lookback.Value + 1;

        if (start < 0)
        {
            return AggregateValue.Unknown;
        }

        var count = 0;

        for (var i = start; i <= currentIndex; i++)
        {
            var result = Expression.Evaluate(
                context.At(candles[i].CloseTime));

            if (result.Truth == RuleTruth.Unknown)
            {
                return AggregateValue.Unknown;
            }

            if (result.Truth == RuleTruth.True)
            {
                count++;
            }
        }

        return AggregateValue.From(count);
    }

    private AggregateValue EvaluateCountStreak(
        EvaluationContext context,
        Candles candles,
        int currentIndex)
    {
        if (Threshold < 0)
        {
            throw new InvalidOperationException(
                "CountStreak threshold cannot be negative.");
        }

        var evaluationsRequired =
            AggregateHistory.RequiredStreakEvaluations(
                Compare,
                Threshold);

        if (currentIndex + 1 < evaluationsRequired)
        {
            return AggregateValue.Unknown;
        }

        var streak = 0;
        var inspected = 0;

        for (var i = currentIndex;
             i >= 0 && inspected < evaluationsRequired;
             i--, inspected++)
        {
            var result = Expression.Evaluate(
                context.At(candles[i].CloseTime));

            if (result.Truth == RuleTruth.Unknown)
            {
                return AggregateValue.Unknown;
            }

            if (result.Truth == RuleTruth.False)
            {
                break;
            }

            streak++;
        }

        return AggregateValue.From(streak);
    }

    private static bool CompareInt(
        int left,
        ComparisonOperator op,
        int right)
        => op switch
        {
            ComparisonOperator.GreaterThan => left > right,
            ComparisonOperator.GreaterOrEqual => left >= right,
            ComparisonOperator.LessThan => left < right,
            ComparisonOperator.LessOrEqual => left <= right,
            ComparisonOperator.Equal => left == right,
            ComparisonOperator.NotEqual => left != right,
            _ => throw new ArgumentOutOfRangeException(nameof(op))
        };

    private readonly record struct AggregateValue(
        bool HasValue,
        int Value)
    {
        public static AggregateValue Unknown => new(false, 0);
        public static AggregateValue From(int value) => new(true, value);
    }
}

internal static class AggregateHistory
{
    public static int RequiredStreakEvaluations(
        ComparisonOperator compare,
        int threshold)
    {
        if (threshold < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(threshold));
        }

        var required = compare switch
        {
            ComparisonOperator.GreaterOrEqual => threshold,
            ComparisonOperator.GreaterThan => threshold + 1,
            ComparisonOperator.LessThan => threshold,
            ComparisonOperator.LessOrEqual => threshold + 1,
            ComparisonOperator.Equal => threshold + 1,
            ComparisonOperator.NotEqual => threshold + 1,
            _ => throw new ArgumentOutOfRangeException(nameof(compare))
        };

        return Math.Max(1, required);
    }
}