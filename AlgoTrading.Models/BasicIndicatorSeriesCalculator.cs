using System;

namespace AlgoTrading.Models
{
    public sealed class BasicIndicatorSeriesCalculator : IIndicatorSeriesCalculator
    {
        public BasicIndicatorSeriesCalculator(CandleSourceSeriesReader sourceReader)
        {
            if (sourceReader == null)
            {
                throw new ArgumentNullException(nameof(sourceReader));
            }
        }

        public double[] Calculate(IndicatorDependency dependency, AlgoTrading.Models.Rules.RuleMarketData data)
        {
            if (dependency == null)
            {
                throw new ArgumentNullException(nameof(dependency));
            }
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }
            return CalculateWithCache(dependency, data, new IndicatorCalculationCache(), "direct");
        }

        internal double[] CalculateWithCache(IndicatorDependency dependency, AlgoTrading.Models.Rules.RuleMarketData data,
            IndicatorCalculationCache cache, string revision)
        {
            ValidateLength(dependency.Length);
            if (!double.IsFinite(dependency.Multiplier) || dependency.Multiplier <= 0.0)
            {
                throw new ArgumentOutOfRangeException(nameof(dependency), "Multiplier must be finite and positive, matching rule binding.");
            }
            AlgoTrading.Models.Rules.IndicatorValue indicator = new AlgoTrading.Models.Rules.IndicatorValue(
                dependency.Function, dependency.Timeframe, dependency.Length, dependency.Source, dependency.Multiplier,
                dependency.Period2, dependency.Period3, dependency.Component, dependency.Smoothing);
            AlgoTrading.Models.Rules.ComparisonCondition comparison = new AlgoTrading.Models.Rules.ComparisonCondition(
                indicator, AlgoTrading.Models.Rules.ComparisonOperator.GreaterThan, new AlgoTrading.Models.Rules.NumberValue(0));
            AlgoTrading.Models.Rules.RuleDefinition definition = new AlgoTrading.Models.Rules.RuleDefinition(
                "Declared indicator", dependency.Timeframe, comparison);
            AlgoTrading.Models.Rules.BoundRule bound = AlgoTrading.Models.Rules.RuleBinder.Bind(definition);
            AlgoTrading.Models.Rules.BoundRuleExecution execution = bound.BindData(data, cache, revision, IndicatorCalculationVersion.CompletedWarmupV1);
            return execution.CopyIndicatorSeries(indicator);
        }

        internal double[] CalculateSeries(IndicatorDependency dependency, double[] source)
        {
            switch (dependency.Function)
            {
                case AlgoTrading.Models.Rules.IndicatorFunction.SimpleMovingAverage:
                    return CalculateSimpleMovingAverage(source, dependency.Length);
                case AlgoTrading.Models.Rules.IndicatorFunction.ExponentialMovingAverage:
                    return CalculateExponentialMovingAverage(source, dependency.Length);
                case AlgoTrading.Models.Rules.IndicatorFunction.RelativeStrengthIndex:
                    return CalculateRelativeStrengthIndex(source, dependency.Length);
                case AlgoTrading.Models.Rules.IndicatorFunction.BollingerMiddleBand:
                    return CalculateBollingerBand(source, dependency.Length, dependency.Multiplier, 0);
                case AlgoTrading.Models.Rules.IndicatorFunction.BollingerUpperBand:
                    return CalculateBollingerBand(source, dependency.Length, dependency.Multiplier, 1);
                case AlgoTrading.Models.Rules.IndicatorFunction.BollingerLowerBand:
                    return CalculateBollingerBand(source, dependency.Length, dependency.Multiplier, -1);
                default:
                    throw new NotSupportedException("This calculator batch supports SMA, EMA, RSI, and Bollinger Bands only.");
            }
        }

        private double[] CalculateSimpleMovingAverage(double[] source, int length)
        {
            ValidateLength(length);
            double[] result = CreateUnavailableSeries(source.Length);
            if (source.Length < length)
            {
                return result;
            }
            for (int index = length - 1; index < source.Length; index++)
            {
                double sum = 0.0;
                bool available = true;
                for (int window = index - length + 1; window <= index; window++)
                {
                    if (!double.IsFinite(source[window]))
                    {
                        available = false;
                        break;
                    }
                    sum += source[window];
                }
                if (available)
                {
                    result[index] = sum / length;
                }
            }
            return result;
        }

        private double[] CalculateExponentialMovingAverage(double[] source, int length)
        {
            ValidateLength(length);
            double[] result = CreateUnavailableSeries(source.Length);
            if (source.Length < length)
            {
                return result;
            }
            double seed = 0.0;
            int seedCount = 0;
            double previous = double.NaN;
            double smoothing = 2.0 / (length + 1.0);
            for (int index = 0; index < source.Length; index++)
            {
                if (!double.IsFinite(source[index]))
                {
                    seed = 0.0;
                    seedCount = 0;
                    previous = double.NaN;
                    continue;
                }
                if (seedCount < length)
                {
                    seed += source[index];
                    seedCount++;
                    if (seedCount < length)
                    {
                        continue;
                    }
                    previous = seed / length;
                }
                else
                {
                    previous = ((source[index] - previous) * smoothing) + previous;
                }
                result[index] = previous;
            }
            return result;
        }

        private double[] CalculateRelativeStrengthIndex(double[] source, int length)
        {
            ValidateLength(length);
            AlgoTrading.Models.Rules.IndicatorValue indicator = new AlgoTrading.Models.Rules.IndicatorValue(
                AlgoTrading.Models.Rules.IndicatorFunction.RelativeStrengthIndex, "source", length);
            return AlgoTrading.Models.Rules.RuleSeriesCalculations.Calculate(indicator, source);
        }

        private double[] CalculateBollingerBand(double[] source, int length, double multiplier, int direction)
        {
            ValidateLength(length);
            if (!double.IsFinite(multiplier) || multiplier < 0.0)
            {
                throw new ArgumentOutOfRangeException(nameof(multiplier), "Bollinger multiplier must be finite and nonnegative.");
            }
            double[] result = CreateUnavailableSeries(source.Length);
            for (int index = length - 1; index < source.Length; index++)
            {
                double sum = 0.0;
                for (int windowIndex = index - length + 1; windowIndex <= index; windowIndex++)
                {
                    sum += source[windowIndex];
                }
                double average = sum / length;
                double squaredDifference = 0.0;
                for (int windowIndex = index - length + 1; windowIndex <= index; windowIndex++)
                {
                    double difference = source[windowIndex] - average;
                    squaredDifference += difference * difference;
                }
                double standardDeviation = Math.Sqrt(squaredDifference / length);
                result[index] = average + (direction * multiplier * standardDeviation);
            }
            return result;
        }

        private double[] CreateUnavailableSeries(int count)
        {
            double[] result = new double[count];
            for (int index = 0; index < result.Length; index++)
            {
                result[index] = double.NaN;
            }
            return result;
        }

        private void ValidateLength(int length)
        {
            if (length <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(length), "Indicator length must be positive.");
            }
        }
    }
}
