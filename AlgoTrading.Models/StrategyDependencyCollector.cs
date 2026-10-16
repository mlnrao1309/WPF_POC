using System;
using System.Collections.Generic;
using AlgoTrading.Models.Rules;

namespace AlgoTrading.Models
{
    public sealed class StrategyDependencyCollector
    {
        public IndicatorDependencyPlan Collect(RuleDefinition definition)
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }
            RuleBinder.Bind(definition);
            definition = RuleDefinitionJson.Deserialize(RuleDefinitionJson.Serialize(definition));
            HashSet<string> timeframes = new HashSet<string>(StringComparer.Ordinal);
            HashSet<IndicatorDependency> knownIndicators = new HashSet<IndicatorDependency>();
            List<IndicatorDependency> indicators = new List<IndicatorDependency>();
            AddTimeframe(timeframes, definition.EvaluationTimeframe);
            VisitCondition(definition.Condition, timeframes, knownIndicators, indicators);
            List<string> orderedTimeframes = new List<string>(timeframes);
            orderedTimeframes.Sort(StringComparer.Ordinal);
            return new IndicatorDependencyPlan(orderedTimeframes.AsReadOnly(), indicators.AsReadOnly());
        }

        private void AddTimeframe(HashSet<string> timeframes, string timeframe)
        {
            if (string.IsNullOrWhiteSpace(timeframe))
            {
                throw new ArgumentException("Every dependency must specify a timeframe.");
            }
            timeframes.Add(timeframe);
        }

        private void VisitCondition(ConditionDefinition condition, HashSet<string> timeframes,
            HashSet<IndicatorDependency> knownIndicators, List<IndicatorDependency> indicators)
        {
            if (condition == null || !condition.Enabled)
            {
                return;
            }
            if (condition is ComparisonCondition comparison)
            {
                VisitValue(comparison.Left, timeframes, knownIndicators, indicators);
                VisitValue(comparison.Right, timeframes, knownIndicators, indicators);
                return;
            }
            if (condition is ConditionGroup group)
            {
                foreach (ConditionDefinition child in group.Conditions)
                {
                    VisitCondition(child, timeframes, knownIndicators, indicators);
                }
                return;
            }
            if (condition is NotCondition negation)
            {
                VisitCondition(negation.Condition, timeframes, knownIndicators, indicators);
                return;
            }
            throw new ArgumentException("Unknown condition definition.");
        }

        private void VisitValue(ValueDefinition value, HashSet<string> timeframes,
            HashSet<IndicatorDependency> knownIndicators, List<IndicatorDependency> indicators)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }
            if (value is CandleValue candle)
            {
                AddTimeframe(timeframes, candle.Timeframe);
                return;
            }
            if (value is IndicatorValue indicator)
            {
                AddTimeframe(timeframes, indicator.Timeframe);
                if (indicator.Source != null)
                {
                    VisitValue(indicator.Source, timeframes, knownIndicators, indicators);
                }
                IndicatorDependency dependency = new IndicatorDependency(indicator);
                if (knownIndicators.Add(dependency))
                {
                    indicators.Add(dependency);
                }
                return;
            }
            if (value is ArithmeticValue arithmetic)
            {
                VisitValue(arithmetic.Left, timeframes, knownIndicators, indicators);
                VisitValue(arithmetic.Right, timeframes, knownIndicators, indicators);
                return;
            }
            if (value is RoundValue round)
            {
                VisitValue(round.Source, timeframes, knownIndicators, indicators);
                return;
            }
            if (value is PreviousValue previous)
            {
                AddTimeframe(timeframes, previous.Timeframe);
                VisitValue(previous.Source, timeframes, knownIndicators, indicators);
                return;
            }
            if (value is CountValue count)
            {
                AddTimeframe(timeframes, count.Timeframe);
                VisitCondition(count.Condition, timeframes, knownIndicators, indicators);
                return;
            }
            if (value is NumberValue)
            {
                return;
            }
            throw new ArgumentException("Unknown value definition.");
        }
    }
}
