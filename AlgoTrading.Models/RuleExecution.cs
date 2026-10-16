using System;
using System.Collections.Generic;

namespace AlgoTrading.Models.Rules
{
    /// <summary>Thread-safe evaluation against a fixed data snapshot. Create a new binding when finalized market data changes.</summary>
    public sealed class BoundRuleExecution
    {
        private readonly RuleDefinition definition;
        private readonly RuleMarketData data;
        private readonly Dictionary<IndicatorValue, double[]> indicatorSeries = new Dictionary<IndicatorValue, double[]>();
        private readonly object evaluationLock = new object();
        private readonly IndicatorCalculationCache sharedCache;
        private readonly string dataRevision;
        private readonly IndicatorCalculationVersion version;
        private readonly BasicIndicatorSeriesCalculator warmupCalculator;
        internal BoundRuleExecution(RuleDefinition definition, RuleMarketData data,
            IndicatorCalculationCache cache, string dataRevision, IndicatorCalculationVersion version)
        {
            this.definition = definition;
            this.data = data;
            this.sharedCache = cache;
            this.dataRevision = dataRevision + ":" + data.Fingerprint;
            this.version = version;
            this.warmupCalculator = new BasicIndicatorSeriesCalculator(new CandleSourceSeriesReader());
        }

        public RuleEvaluation Evaluate(DateTimeOffset asOf)
        {
            lock (evaluationLock)
            {
                CompletedCandle[] clock = data.GetSeries(definition.EvaluationTimeframe);
                int evaluationIndex = RuleMarketData.FindCompletedIndex(clock, asOf);
                if (evaluationIndex < 0)
                {
                    return new RuleEvaluation(RuleStatus.InsufficientData, null, "No completed evaluation candle is available.");
                }
                DateTimeOffset evaluationTime = clock[evaluationIndex].ClosedAt;
                ConditionEvaluationResult result = EvaluateCondition(definition.Condition, evaluationTime, definition.EvaluationTimeframe);
                return new RuleEvaluation(result.Status, evaluationTime, result.Explanation);
            }
        }

        internal double SampleValue(ValueDefinition value, DateTimeOffset timestamp)
        {
            lock (evaluationLock)
            {
                return EvaluateValue(value, timestamp);
            }
        }

        internal double[] CopyIndicatorSeries(IndicatorValue indicator)
        {
            lock (evaluationLock)
            {
                return (double[])GetIndicatorSeries(indicator).Clone();
            }
        }

        internal RuleStatus SampleCondition(ComparisonCondition condition, DateTimeOffset timestamp, string timeframe)
        {
            lock (evaluationLock)
            {
                return EvaluateCondition(condition, timestamp, timeframe).Status;
            }
        }

        private ConditionEvaluationResult EvaluateCondition(ConditionDefinition condition, DateTimeOffset timestamp, string clockTimeframe)
        {
            switch (condition)
            {
                case ConditionGroup group:
                    return EvaluateGroup(group, timestamp, clockTimeframe);
                case NotCondition negation:
                    ConditionEvaluationResult negated = EvaluateCondition(negation.Condition, timestamp, clockTimeframe);
                    RuleStatus status = RuleStatus.InsufficientData;
                    if (negated.Status == RuleStatus.Matched)
                    {
                        status = RuleStatus.NotMatched;
                    }
                    else if (negated.Status == RuleStatus.NotMatched)
                    {
                        status = RuleStatus.Matched;
                    }
                    return new ConditionEvaluationResult(status, $"Not ({negated.Explanation})");
                case ComparisonCondition comparison:
                    return EvaluateComparison(comparison, timestamp, clockTimeframe);
                default:
                    throw new InvalidOperationException("Unsupported condition.");
            }
        }

        private ConditionEvaluationResult EvaluateGroup(ConditionGroup group, DateTimeOffset timestamp, string clockTimeframe)
        {
            bool hasUnavailableChild = false;
            List<string> explanations = new List<string>();
            List<ConditionDefinition> children = new List<ConditionDefinition>();
            List<ConditionDefinition> crossoverChildren = new List<ConditionDefinition>();
            // Stable partition within All groups only; never cross a group boundary.
            foreach (ConditionDefinition child in group.Conditions)
            {
                if (!child.Enabled)
                {
                    continue;
                }
                if (group.Operator == GroupOperator.All && ContainsCrossover(child))
                {
                    crossoverChildren.Add(child);
                }
                else
                {
                    children.Add(child);
                }
            }
            children.AddRange(crossoverChildren);
            foreach (ConditionDefinition child in children)
            {
                ConditionEvaluationResult result = EvaluateCondition(child, timestamp, clockTimeframe);
                explanations.Add(result.Explanation);
                if (group.Operator == GroupOperator.All && result.Status == RuleStatus.NotMatched)
                {
                    return result;
                }
                if (group.Operator == GroupOperator.Any && result.Status == RuleStatus.Matched)
                {
                    return result;
                }
                if (result.Status == RuleStatus.InsufficientData)
                {
                    hasUnavailableChild = true;
                }
            }
            RuleStatus status = RuleStatus.NotMatched;
            if (hasUnavailableChild)
            {
                status = RuleStatus.InsufficientData;
            }
            else if (group.Operator == GroupOperator.All)
            {
                status = RuleStatus.Matched;
            }
            return new ConditionEvaluationResult(status, string.Join("; ", explanations));
        }

        private ConditionEvaluationResult EvaluateComparison(ComparisonCondition comparison, DateTimeOffset timestamp, string clockTimeframe)
        {
            double left = EvaluateValue(comparison.Left, timestamp);
            double right = EvaluateValue(comparison.Right, timestamp);
            string description = RuleLanguage.Describe(comparison);
            if (!double.IsFinite(left) || !double.IsFinite(right))
            {
                return new ConditionEvaluationResult(RuleStatus.InsufficientData, $"Unavailable: {description}. Check candle history and calculation inputs.");
            }
            bool matches;
            string previousDescription = "";
            if (comparison.Operator == ComparisonOperator.CrossedAbove || comparison.Operator == ComparisonOperator.CrossedBelow)
            {
                CompletedCandle[] clock = data.GetSeries(clockTimeframe);
                int currentIndex = RuleMarketData.FindCompletedIndex(clock, timestamp);
                if (currentIndex < 1)
                {
                    return new ConditionEvaluationResult(RuleStatus.InsufficientData, $"Unavailable: {description}. A previous completed candle is required.");
                }
                DateTimeOffset previousTimestamp = clock[currentIndex - 1].ClosedAt;
                double previousLeft = EvaluateValue(comparison.Left, previousTimestamp);
                double previousRight = EvaluateValue(comparison.Right, previousTimestamp);
                if (!double.IsFinite(previousLeft) || !double.IsFinite(previousRight))
                {
                    return new ConditionEvaluationResult(RuleStatus.InsufficientData, $"Unavailable: {description}. Previous indicator values are not ready.");
                }
                if (comparison.Operator == ComparisonOperator.CrossedAbove)
                {
                    matches = previousLeft <= previousRight && left > right;
                }
                else
                {
                    matches = previousLeft >= previousRight && left < right;
                }
                previousDescription = $"; previous values {previousLeft:G8} and {previousRight:G8}";
            }
            else
            {
                matches = Compare(comparison.Operator, left, right);
            }
            RuleStatus status = RuleStatus.NotMatched;
            string label = "Not matched";
            if (matches)
            {
                status = RuleStatus.Matched;
                label = "Matched";
            }
            return new ConditionEvaluationResult(status, $"{label}: {description} (current values {left:G8} and {right:G8}{previousDescription}).");
        }

        private static bool Compare(ComparisonOperator operation, double left, double right)
        {
            switch (operation)
            {
                case ComparisonOperator.GreaterThan:
                    return left > right;
                case ComparisonOperator.GreaterThanOrEqual:
                    return left >= right;
                case ComparisonOperator.LessThan:
                    return left < right;
                case ComparisonOperator.LessThanOrEqual:
                    return left <= right;
                case ComparisonOperator.Equal:
                    return left == right;
                case ComparisonOperator.NotEqual:
                    return left != right;
                default:
                    throw new InvalidOperationException("Unsupported comparison.");
            }
        }

        private static bool ContainsCrossover(ConditionDefinition condition)
        {
            switch (condition)
            {
                case ComparisonCondition comparison:
                    return comparison.Operator == ComparisonOperator.CrossedAbove
                        || comparison.Operator == ComparisonOperator.CrossedBelow
                        || ContainsCrossover(comparison.Left) || ContainsCrossover(comparison.Right);
                case ConditionGroup group:
                    foreach (ConditionDefinition child in group.Conditions)
                    {
                        if (child.Enabled && ContainsCrossover(child))
                        {
                            return true;
                        }
                    }
                    return false;
                case NotCondition negation:
                    return ContainsCrossover(negation.Condition);
                default:
                    return false;
            }
        }

        private static bool ContainsCrossover(ValueDefinition value)
        {
            switch (value)
            {
                case CountValue count:
                    return ContainsCrossover(count.Condition);
                case IndicatorValue indicator:
                    return indicator.Source != null && ContainsCrossover(indicator.Source);
                case ArithmeticValue arithmetic:
                    return ContainsCrossover(arithmetic.Left) || ContainsCrossover(arithmetic.Right);
                case RoundValue round:
                    return ContainsCrossover(round.Source);
                case PreviousValue previous:
                    return ContainsCrossover(previous.Source);
                default:
                    return false;
            }
        }

        private double EvaluateValue(ValueDefinition value, DateTimeOffset timestamp)
        {
            double result;
            switch (value)
            {
                case NumberValue number:
                    return number.Value;
                case CandleValue candle:
                    CompletedCandle[] candles = data.GetSeries(candle.Timeframe);
                    int candleIndex = RuleMarketData.FindCompletedIndex(candles, timestamp);
                    if (candleIndex < candle.CandlesAgo)
                    {
                        return double.NaN;
                    }
                    Candle selected = candles[candleIndex - candle.CandlesAgo].Candle;
                    switch (candle.Field)
                    {
                        case CandleField.Open:
                            return (double)selected.Open;
                        case CandleField.High:
                            return (double)selected.High;
                        case CandleField.Low:
                            return (double)selected.Low;
                        case CandleField.Close:
                            return (double)selected.Close;
                        case CandleField.Volume:
                            return selected.Volume;
                        default:
                            return double.NaN;
                    }
                case IndicatorValue indicator:
                    int indicatorIndex = RuleMarketData.FindCompletedIndex(data.GetSeries(indicator.Timeframe), timestamp);
                    if (indicatorIndex < 0)
                    {
                        return double.NaN;
                    }
                    return GetIndicatorSeries(indicator)[indicatorIndex];
                case ArithmeticValue arithmetic:
                    double left = EvaluateValue(arithmetic.Left, timestamp);
                    double right = EvaluateValue(arithmetic.Right, timestamp);
                    switch (arithmetic.Operator)
                    {
                        case ArithmeticOperator.Add:
                            result = left + right;
                            break;
                        case ArithmeticOperator.Subtract:
                            result = left - right;
                            break;
                        case ArithmeticOperator.Multiply:
                            result = left * right;
                            break;
                        case ArithmeticOperator.Divide:
                            result = double.NaN;
                            if (right != 0)
                            {
                                result = left / right;
                            }
                            break;
                        default:
                            result = double.NaN;
                            break;
                    }
                    break;
                case RoundValue round:
                    result = Math.Round(EvaluateValue(round.Source, timestamp), round.DecimalPlaces, MidpointRounding.AwayFromZero);
                    break;
                case PreviousValue previous:
                    CompletedCandle[] previousClock = data.GetSeries(previous.Timeframe);
                    int previousIndex = RuleMarketData.FindCompletedIndex(previousClock, timestamp);
                    if (previousIndex < previous.CandlesAgo)
                    {
                        return double.NaN;
                    }
                    return EvaluateValue(previous.Source, previousClock[previousIndex - previous.CandlesAgo].ClosedAt);
                case CountValue count:
                    CompletedCandle[] countClock = data.GetSeries(count.Timeframe);
                    int endIndex = RuleMarketData.FindCompletedIndex(countClock, timestamp);
                    if (endIndex < count.CandlesAgo)
                    {
                        return double.NaN;
                    }
                    endIndex -= count.CandlesAgo;
                    if (endIndex < count.Length - 1)
                    {
                        return double.NaN;
                    }
                    int matchCount = 0;
                    for (int windowIndex = endIndex - count.Length + 1; windowIndex <= endIndex; windowIndex++)
                    {
                        ConditionEvaluationResult counted = EvaluateCondition(count.Condition, countClock[windowIndex].ClosedAt, count.Timeframe);
                        if (counted.Status == RuleStatus.InsufficientData)
                        {
                            return double.NaN;
                        }
                        if (counted.Status == RuleStatus.Matched)
                        {
                            matchCount++;
                        }
                    }
                    return matchCount;
                default:
                    throw new InvalidOperationException("Unsupported calculation.");
            }
            if (double.IsFinite(result))
            {
                return result;
            }
            return double.NaN;
        }

        private double[] GetIndicatorSeries(IndicatorValue indicator)
        {
            if (indicatorSeries.TryGetValue(indicator, out double[]? cached))
            {
                return cached;
            }
            CompletedCandle[] candles = data.GetSeries(indicator.Timeframe);
            IndicatorDependency dependency = new IndicatorDependency(indicator);
            IndicatorCalculationCacheKey key = new IndicatorCalculationCacheKey(data.InstrumentToken,
                dependency, this.version.ToString(), this.dataRevision);
            double[]? shared;
            if (this.sharedCache.TryGet(key, out shared) && shared != null)
            {
                indicatorSeries.Add(indicator, shared);
                return shared;
            }
            double[] calculated;
            if (CoreIndicatorSeriesCalculator.Supports(indicator.Function))
            {
                calculated = CoreIndicatorSeriesCalculator.Calculate(indicator, candles);
            }
            else if (indicator.Function == IndicatorFunction.SuperTrend)
            {
                calculated = RuleSeriesCalculations.SuperTrend(candles, indicator.Length, indicator.Multiplier);
            }
            else
            {
                ValueDefinition? source = indicator.Source;
                if (source == null)
                {
                    source = new CandleValue(indicator.Timeframe);
                }
                // Every source sample is evaluated as of that candle's close. Later rows never affect an earlier result.
                double[] sourceValues = new double[candles.Length];
                for (int index = 0; index < candles.Length; index++)
                {
                    sourceValues[index] = EvaluateValue(source, candles[index].ClosedAt);
                }
                if (this.version == IndicatorCalculationVersion.CompletedWarmupV1)
                {
                    calculated = this.warmupCalculator.CalculateSeries(dependency, sourceValues);
                }
                else
                {
                    calculated = RuleSeriesCalculations.Calculate(indicator, sourceValues);
                }
            }
            this.sharedCache.Set(key, calculated);
            indicatorSeries.Add(indicator, calculated);
            return calculated;
        }
    }
}
