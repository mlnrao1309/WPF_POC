using System;
using System.Collections.Generic;
using System.Globalization;

namespace AlgoTrading.Models.Rules
{
    internal sealed class RuleBindingContext
    {
        private readonly HashSet<string> timeframes = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<IndicatorValue> indicators = new HashSet<IndicatorValue>();
        private int visitedNodeCount;

        internal BoundRule Bind(RuleDefinition definition)
        {
            ArgumentNullException.ThrowIfNull(definition);
            ArgumentException.ThrowIfNullOrWhiteSpace(definition.Name);
            AddTimeframe(definition.EvaluationTimeframe);
            if (definition.Condition == null || !definition.Condition.Enabled)
            {
                throw new ArgumentException("Enable the root condition before binding.");
            }
            CheckCondition(definition.Condition, 0);
            // Detach caller-owned lists before a rule is executed.
            RuleDefinition snapshot = RuleDefinitionJson.Deserialize(RuleDefinitionJson.Serialize(definition));
            List<string> orderedTimeframes = new List<string>(timeframes);
            orderedTimeframes.Sort(StringComparer.Ordinal);
            List<string> calculations = new List<string>();
            HashSet<string> descriptions = new HashSet<string>(StringComparer.Ordinal);
            foreach (IndicatorValue indicator in indicators)
            {
                string description = RuleLanguage.Describe(indicator);
                if (descriptions.Add(description))
                {
                    calculations.Add(description);
                }
            }
            RuleBindingPlan plan = new RuleBindingPlan(orderedTimeframes.AsReadOnly(), calculations.AsReadOnly());
            return new BoundRule(snapshot, plan);
        }

        private static bool HasEnabledCondition(IReadOnlyList<ConditionDefinition> conditions)
        {
            foreach (ConditionDefinition condition in conditions)
            {
                if (condition != null && condition.Enabled)
                {
                    return true;
                }
            }
            return false;
        }

        private void CheckDepth(int depth)
        {
            if (depth > 48 || ++visitedNodeCount > 2048)
            {
                throw new ArgumentException("The rule is too large. Use at most 48 nesting levels and 2048 expressions.");
            }
        }

        private void AddTimeframe(string timeframe)
        {
            if (string.IsNullOrWhiteSpace(timeframe))
            {
                throw new ArgumentException("Choose a timeframe for every candle and indicator.");
            }
            timeframes.Add(timeframe);
        }

        private void CheckEnum<T>(T value) where T : struct, Enum
        {
            if (!Enum.IsDefined(value))
            {
                throw new ArgumentException($"Unsupported {typeof(T).Name}: {value}.");
            }
        }

        private void CheckCondition(ConditionDefinition condition, int depth)
        {
            ArgumentNullException.ThrowIfNull(condition);
            CheckDepth(depth);
            if (!condition.Enabled)
            {
                return;
            }
            switch (condition)
            {
                case ComparisonCondition comparison:
                    CheckEnum(comparison.Operator);
                    CheckValue(comparison.Left, depth + 1);
                    CheckValue(comparison.Right, depth + 1);
                    break;
                case ConditionGroup group:
                    CheckEnum(group.Operator);
                    if (group.Conditions == null || group.Conditions.Count == 0 || !HasEnabledCondition(group.Conditions))
                    {
                        throw new ArgumentException("A condition group needs at least one enabled condition.");
                    }
                    foreach (ConditionDefinition child in group.Conditions)
                    {
                        CheckCondition(child, depth + 1);
                    }
                    break;
                case NotCondition negation:
                    if (negation.Condition == null || !negation.Condition.Enabled)
                    {
                        throw new ArgumentException("Not needs an enabled condition.");
                    }
                    CheckCondition(negation.Condition, depth + 1);
                    break;
                default:
                    throw new ArgumentException("Unknown condition type.");
            }
        }

        private void CheckValue(ValueDefinition value, int depth)
        {
            ArgumentNullException.ThrowIfNull(value);
            CheckDepth(depth);
            switch (value)
            {
                case NumberValue number:
                    if (!double.IsFinite(number.Value))
                    {
                        throw new ArgumentException("A number must be finite.");
                    }
                    break;
                case CandleValue candle:
                    CheckEnum(candle.Field);
                    AddTimeframe(candle.Timeframe);
                    if (candle.CandlesAgo < 0)
                    {
                        throw new ArgumentException("Candle offsets cannot refer to the future.");
                    }
                    break;
                case IndicatorValue indicator:
                    CheckEnum(indicator.Function);
                    AddTimeframe(indicator.Timeframe);
                    if (indicator.Length <= 0)
                    {
                        throw new ArgumentException("An indicator length must be positive.");
                    }
                    if (!double.IsFinite(indicator.Multiplier) || indicator.Multiplier <= 0)
                    {
                        throw new ArgumentException("An indicator multiplier must be finite and positive.");
                    }
                    if (indicator.Period2 < 0 || indicator.Period3 < 0)
                    {
                        throw new ArgumentException("Secondary indicator periods cannot be negative.");
                    }
                    CheckEnum(indicator.Component);
                    if (indicator.Smoothing.HasValue) CheckEnum(indicator.Smoothing.Value);
                    if (CoreIndicatorSeriesCalculator.Supports(indicator.Function)
                        && indicator.Source is not null and not CandleValue)
                    {
                        throw new ArgumentException("Core indicators require a candle-field source.");
                    }
                    if (indicator.Function == IndicatorFunction.SuperTrend && indicator.Source != null)
                    {
                        throw new ArgumentException("SuperTrend uses high, low and close together; do not choose a separate source.");
                    }
                    if (indicator.Source != null)
                    {
                        CheckValue(indicator.Source, depth + 1);
                    }
                    indicators.Add(indicator);
                    break;
                case ArithmeticValue arithmetic:
                    CheckEnum(arithmetic.Operator);
                    CheckValue(arithmetic.Left, depth + 1);
                    CheckValue(arithmetic.Right, depth + 1);
                    break;
                case RoundValue round:
                    if (round.DecimalPlaces < 0 || round.DecimalPlaces > 15)
                    {
                        throw new ArgumentException("Round supports zero through fifteen decimal places.");
                    }
                    CheckValue(round.Source, depth + 1);
                    break;
                case PreviousValue previous:
                    AddTimeframe(previous.Timeframe);
                    if (previous.CandlesAgo < 0)
                    {
                        throw new ArgumentException("Candle offsets cannot refer to the future.");
                    }
                    CheckValue(previous.Source, depth + 1);
                    break;
                case CountValue count:
                    AddTimeframe(count.Timeframe);
                    if (count.Length <= 0 || count.CandlesAgo < 0)
                    {
                        throw new ArgumentException("Count needs a positive window and a nonnegative offset.");
                    }
                    if (count.Condition == null || !count.Condition.Enabled)
                    {
                        throw new ArgumentException("Count needs an enabled condition.");
                    }
                    CheckCondition(count.Condition, depth + 1);
                    break;
                default:
                    throw new ArgumentException("Unknown calculation type.");
            }
        }

    }
}
