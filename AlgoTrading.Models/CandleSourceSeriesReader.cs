using System;
using AlgoTrading.Models.Rules;

namespace AlgoTrading.Models
{
    public sealed class CandleSourceSeriesReader
    {
        public double[] Read(IndicatorDependency dependency, RuleMarketData data)
        {
            if (dependency == null)
            {
                throw new ArgumentNullException(nameof(dependency));
            }
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }
            CandleValue? source = dependency.Source as CandleValue;
            if (dependency.Source == null)
            {
                source = new CandleValue(dependency.Timeframe, CandleField.Close);
            }
            if (source == null)
            {
                throw new NotSupportedException("This batch supports indicators whose source is a candle field.");
            }
            if (!string.Equals(source.Timeframe, dependency.Timeframe, StringComparison.Ordinal))
            {
                throw new ArgumentException("The indicator source timeframe must match the indicator timeframe.");
            }
            if (source.CandlesAgo != 0)
            {
                throw new NotSupportedException("A shifted source is not supported by the base indicator calculator.");
            }

            CompletedCandle[] candles = data.GetSeries(dependency.Timeframe);
            double[] values = new double[candles.Length];
            for (int candleIndex = 0; candleIndex < candles.Length; candleIndex++)
            {
                values[candleIndex] = ReadField(candles[candleIndex].Candle, source.Field);
            }
            return values;
        }

        private double ReadField(Candle candle, CandleField field)
        {
            switch (field)
            {
                case CandleField.Open:
                    return (double)candle.Open;
                case CandleField.High:
                    return (double)candle.High;
                case CandleField.Low:
                    return (double)candle.Low;
                case CandleField.Close:
                    return (double)candle.Close;
                case CandleField.Volume:
                    return candle.Volume;
                default:
                    throw new ArgumentException("Unknown candle field.", nameof(field));
            }
        }
    }
}
