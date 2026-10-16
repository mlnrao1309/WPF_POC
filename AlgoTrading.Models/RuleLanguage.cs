using System;
using System.Collections.Generic;
using System.Globalization;

namespace AlgoTrading.Models.Rules
{
    public static class RuleLanguage
    {
        public static string Describe(ConditionDefinition condition)
        {
            switch (condition)
            {
                case ComparisonCondition comparison:
                    return $"{Describe(comparison.Left)} {ComparisonLabel(comparison.Operator)} {Describe(comparison.Right)}";
                case ConditionGroup group:
                    string label = "Any";
                    if (group.Operator == GroupOperator.All)
                    {
                        label = "All";
                    }
                    List<string> descriptions = new List<string>();
                    foreach (ConditionDefinition child in group.Conditions)
                    {
                        if (child.Enabled)
                        {
                            descriptions.Add(Describe(child));
                        }
                    }
                    return label + " of (" + string.Join("; ", descriptions) + ")";
                case NotCondition negation:
                    return $"Not ({Describe(negation.Condition)})";
                default:
                    throw new ArgumentException("Unknown condition.");
            }
        }

        public static string Describe(ValueDefinition value)
        {
            switch (value)
            {
                case NumberValue number:
                    return number.Value.ToString(CultureInfo.InvariantCulture);
                case CandleValue candle:
                    string candleDescription = candle.Timeframe + " " + candle.Field.ToString().ToLowerInvariant();
                    if (candle.CandlesAgo != 0)
                    {
                        candleDescription += $" ({candle.CandlesAgo} candles ago)";
                    }
                    return candleDescription;
                case IndicatorValue indicator:
                    return DescribeIndicator(indicator);
                case ArithmeticValue arithmetic:
                    return $"({Describe(arithmetic.Left)} {ArithmeticLabel(arithmetic.Operator)} {Describe(arithmetic.Right)})";
                case RoundValue round:
                    return $"Round ({Describe(round.Source)}, {round.DecimalPlaces} decimal places)";
                case PreviousValue previous:
                    return $"{Describe(previous.Source)} at {previous.CandlesAgo} previous {previous.Timeframe} candles";
                case CountValue count:
                    return $"Count ({Describe(count.Condition)}) over {count.Length} {count.Timeframe} candles, ending {count.CandlesAgo} candles ago";
                default:
                    throw new ArgumentException("Unknown value.");
            }
        }

        private static string DescribeIndicator(IndicatorValue indicator)
        {
            string description = $"{IndicatorLabel(indicator.Function)} (length {indicator.Length}, timeframe {indicator.Timeframe}";
            if (indicator.Function == IndicatorFunction.SuperTrend
                || indicator.Function == IndicatorFunction.BollingerUpperBand
                || indicator.Function == IndicatorFunction.BollingerLowerBand)
            {
                description += ", multiplier " + indicator.Multiplier.ToString(CultureInfo.InvariantCulture);
            }
            if (indicator.Function != IndicatorFunction.SuperTrend)
            {
                ValueDefinition? source = indicator.Source;
                if (source == null)
                {
                    source = new CandleValue(indicator.Timeframe);
                }
                description += ", source " + Describe(source);
            }
            return description + ")";
        }

        public static string ComparisonLabel(ComparisonOperator operation)
        {
            switch (operation)
            {
                case ComparisonOperator.GreaterThan:
                    return "is greater than";
                case ComparisonOperator.GreaterThanOrEqual:
                    return "is greater than or equal to";
                case ComparisonOperator.LessThan:
                    return "is less than";
                case ComparisonOperator.LessThanOrEqual:
                    return "is less than or equal to";
                case ComparisonOperator.Equal:
                    return "equals";
                case ComparisonOperator.NotEqual:
                    return "does not equal";
                case ComparisonOperator.CrossedAbove:
                    return "crossed above";
                case ComparisonOperator.CrossedBelow:
                    return "crossed below";
                default:
                    throw new ArgumentException("Unknown comparison.");
            }
        }

        public static string IndicatorLabel(IndicatorFunction function)
        {
            switch (function)
            {
                case IndicatorFunction.SimpleMovingAverage:
                    return "Simple Moving Average";
                case IndicatorFunction.ExponentialMovingAverage:
                    return "Exponential Moving Average";
                case IndicatorFunction.RelativeStrengthIndex:
                    return "Relative Strength Index";
                case IndicatorFunction.BollingerMiddleBand:
                    return "Bollinger middle band";
                case IndicatorFunction.BollingerUpperBand:
                    return "Bollinger upper band";
                case IndicatorFunction.BollingerLowerBand:
                    return "Bollinger lower band";
                case IndicatorFunction.SuperTrend:
                    return "SuperTrend";
                case IndicatorFunction.DoubleExponentialMovingAverage: return "Double Exponential Moving Average";
                case IndicatorFunction.TripleExponentialMovingAverage: return "Triple Exponential Moving Average";
                case IndicatorFunction.WeightedMovingAverage: return "Weighted Moving Average";
                case IndicatorFunction.HullMovingAverage: return "Hull Moving Average";
                case IndicatorFunction.MovingAverageConvergenceDivergence: return "MACD";
                case IndicatorFunction.AverageTrueRange: return "Average True Range";
                case IndicatorFunction.AverageDirectionalIndex: return "Average Directional Index";
                case IndicatorFunction.VolumeWeightedAveragePrice: return "Volume Weighted Average Price";
                case IndicatorFunction.OnBalanceVolume: return "On-Balance Volume";
                case IndicatorFunction.Stochastic: return "Stochastic";
                case IndicatorFunction.CommodityChannelIndex: return "Commodity Channel Index";
                case IndicatorFunction.VolumeSimpleMovingAverage: return "Volume Simple Moving Average";
                case IndicatorFunction.High52Week: return "52-week High";
                case IndicatorFunction.Low52Week: return "52-week Low";
                case IndicatorFunction.RateOfChange: return "Rate of Change";
                case IndicatorFunction.Momentum: return "Momentum";
                case IndicatorFunction.TripleExponentialAverage: return "TRIX";
                case IndicatorFunction.KeltnerChannel: return "Keltner Channel";
                case IndicatorFunction.DonchianChannel: return "Donchian Channel";
                default:
                    throw new ArgumentException("Unknown indicator.");
            }
        }

        private static string ArithmeticLabel(ArithmeticOperator operation)
        {
            switch (operation)
            {
                case ArithmeticOperator.Add:
                    return "+";
                case ArithmeticOperator.Subtract:
                    return "-";
                case ArithmeticOperator.Multiply:
                    return "×";
                case ArithmeticOperator.Divide:
                    return "÷";
                default:
                    throw new ArgumentException("Unknown arithmetic operation.");
            }
        }
    }
}
