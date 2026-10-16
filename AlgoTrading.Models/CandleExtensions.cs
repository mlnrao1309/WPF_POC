using System;
using System.Globalization;
using AlgoTrading.Models;

public static class InstrumentCandleDataExtensions
{
    // Custom selectors require the overload with an explicit source name.
    public static void AddIndicatorData(
        this InstrumentCandleData instrumentData,
        Indicator indicator,
        Func<Candle, decimal>? selector = null,
        params object[] indicatorParameters)
    {
        if (selector != null)
        {
            throw new ArgumentException("Provide a source name when using a custom selector.", nameof(selector));
        }

        AddIndicatorData(instrumentData, indicator, "Close", candle => candle.Close, indicatorParameters);
    }

    /// <summary>
    /// Stores series using indicator, source name and invariant parameter values.
    /// Bollinger bands have separate Middle, Upper and Lower suffixes.
    /// Use a distinct source name for each custom selector.
    /// </summary>
    public static void AddIndicatorData(
        this InstrumentCandleData instrumentData,
        Indicator indicator,
        string sourceName,
        Func<Candle, decimal> selector,
        params object[] indicatorParameters)
    {
        ArgumentNullException.ThrowIfNull(instrumentData.Candles);
        ArgumentNullException.ThrowIfNull(selector);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);
        ArgumentNullException.ThrowIfNull(indicatorParameters);

        int requiredParameterCount;
        switch (indicator)
        {
            case Indicator.SMA:
            case Indicator.EMA:
            case Indicator.RSI:
                requiredParameterCount = 1;
                break;
            case Indicator.BollingerBands:
            case Indicator.SuperTrend:
                requiredParameterCount = 2;
                break;
            default:
                throw new NotSupportedException($"Indicator {indicator} is not implemented.");
        }

        if (indicatorParameters.Length != requiredParameterCount)
        {
            throw new ArgumentException($"Indicator {indicator} requires {requiredParameterCount} parameters.", nameof(indicatorParameters));
        }
        if (indicatorParameters[0] is not int period || period <= 0)
        {
            throw new ArgumentException("The period must be a positive integer.", nameof(indicatorParameters));
        }

        double multiplier = 0;
        if (requiredParameterCount == 2)
        {
            multiplier = indicatorParameters[1] switch
            {
                double value => value,
                float value => value,
                decimal value => (double)value,
                int value => value,
                long value => value,
                _ => throw new ArgumentException("The multiplier must be numeric.", nameof(indicatorParameters))
            };
            if (!double.IsFinite(multiplier) || multiplier <= 0)
            {
                throw new ArgumentException("The multiplier must be finite and positive.", nameof(indicatorParameters));
            }
        }

        string sourceIdentifier = indicator == Indicator.SuperTrend ? "HighLowClose" : Uri.EscapeDataString(sourceName);
        string indicatorKey = $"{indicator}_{sourceIdentifier}_{period.ToString(CultureInfo.InvariantCulture)}";
        if (requiredParameterCount == 2)
        {
            indicatorKey += "_" + multiplier.ToString("R", CultureInfo.InvariantCulture);
        }

        if (indicator == Indicator.BollingerBands)
        {
            var bands = CalculateBollingerBands(instrumentData.Candles, period, multiplier, selector);
            instrumentData.Add(indicatorKey + "_Middle", bands.Middle);
            instrumentData.Add(indicatorKey + "_Upper", bands.Upper);
            instrumentData.Add(indicatorKey + "_Lower", bands.Lower);
            return;
        }

        double[] values = indicator switch
        {
            Indicator.SMA => CalculateSimpleMovingAverage(instrumentData.Candles, period, selector),
            Indicator.EMA => CalculateExponentialMovingAverage(instrumentData.Candles, period, selector),
            Indicator.RSI => CalculateRelativeStrengthIndex(instrumentData.Candles, period, selector),
            Indicator.SuperTrend => CalculateSuperTrend(instrumentData.Candles, period, multiplier),
            _ => throw new NotSupportedException($"Indicator {indicator} is not implemented.")
        };
        instrumentData.Add(indicatorKey, values);
    }

    private static double[] CreateUnavailableSeries(int length)
    {
        double[] values = new double[length];
        Array.Fill(values, double.NaN);
        return values;
    }

    private static double[] CalculateExponentialMovingAverage(Candle[] candles, int period, Func<Candle, decimal> selector)
    {
        double[] movingAverage = new double[candles.Length];
        double multiplier = 2.0 / (period + 1.0);
        for (int candleIndex = 0; candleIndex < candles.Length; candleIndex++)
        {
            double value = (double)selector(candles[candleIndex]);
            movingAverage[candleIndex] = candleIndex == 0
                ? value
                : movingAverage[candleIndex - 1] + multiplier * (value - movingAverage[candleIndex - 1]);
        }
        return movingAverage;
    }

    private static double[] CalculateSimpleMovingAverage(Candle[] candles, int period, Func<Candle, decimal> selector)
    {
        double[] movingAverage = CreateUnavailableSeries(candles.Length);
        double windowSum = 0;
        for (int candleIndex = 0; candleIndex < candles.Length; candleIndex++)
        {
            windowSum += (double)selector(candles[candleIndex]);
            if (candleIndex >= period)
            {
                windowSum -= (double)selector(candles[candleIndex - period]);
            }
            if (candleIndex >= period - 1)
            {
                movingAverage[candleIndex] = windowSum / period;
            }
        }
        return movingAverage;
    }

    private static double[] CalculateRelativeStrengthIndex(Candle[] candles, int period, Func<Candle, decimal> selector)
    {
        double[] relativeStrengthIndex = CreateUnavailableSeries(candles.Length);
        double averageGain = 0;
        double averageLoss = 0;
        for (int candleIndex = 1; candleIndex < candles.Length; candleIndex++)
        {
            double change = (double)(selector(candles[candleIndex]) - selector(candles[candleIndex - 1]));
            double gain = Math.Max(change, 0);
            double loss = Math.Max(-change, 0);
            if (candleIndex <= period)
            {
                averageGain += gain;
                averageLoss += loss;
                if (candleIndex < period)
                {
                    continue;
                }
                averageGain /= period;
                averageLoss /= period;
            }
            else
            {
                averageGain = (averageGain * (period - 1) + gain) / period;
                averageLoss = (averageLoss * (period - 1) + loss) / period;
            }

            // A completely flat series is neutral; gains without losses give 100.
            relativeStrengthIndex[candleIndex] = averageLoss == 0
                ? (averageGain == 0 ? 50 : 100)
                : 100 - 100 / (1 + averageGain / averageLoss);
        }
        return relativeStrengthIndex;
    }

    private static (double[] Middle, double[] Upper, double[] Lower) CalculateBollingerBands(
        Candle[] candles, int period, double multiplier, Func<Candle, decimal> selector)
    {
        double[] middleBand = CalculateSimpleMovingAverage(candles, period, selector);
        double[] upperBand = CreateUnavailableSeries(candles.Length);
        double[] lowerBand = CreateUnavailableSeries(candles.Length);
        for (int candleIndex = period - 1; candleIndex < candles.Length; candleIndex++)
        {
            double squaredDeviationSum = 0;
            for (int windowIndex = candleIndex - period + 1; windowIndex <= candleIndex; windowIndex++)
            {
                double deviation = (double)selector(candles[windowIndex]) - middleBand[candleIndex];
                squaredDeviationSum += deviation * deviation;
            }
            double standardDeviation = Math.Sqrt(squaredDeviationSum / period);
            upperBand[candleIndex] = middleBand[candleIndex] + multiplier * standardDeviation;
            lowerBand[candleIndex] = middleBand[candleIndex] - multiplier * standardDeviation;
        }
        return (middleBand, upperBand, lowerBand);
    }

    private static double[] CalculateSuperTrend(Candle[] candles, int period, double multiplier)
    {
        double[] averageTrueRange = CalculateAverageTrueRange(candles, period);
        double[] superTrend = CreateUnavailableSeries(candles.Length);
        double previousUpperBand = 0;
        double previousLowerBand = 0;
        // Preserve the existing upward direction at the first available candle.
        bool isUpwardTrend = true;
        for (int candleIndex = period - 1; candleIndex < candles.Length; candleIndex++)
        {
            double midpoint = ((double)candles[candleIndex].High + (double)candles[candleIndex].Low) / 2;
            double upperBand = midpoint + multiplier * averageTrueRange[candleIndex];
            double lowerBand = midpoint - multiplier * averageTrueRange[candleIndex];
            if (candleIndex > period - 1)
            {
                double previousClose = (double)candles[candleIndex - 1].Close;
                upperBand = upperBand < previousUpperBand || previousClose > previousUpperBand
                    ? upperBand : previousUpperBand;
                lowerBand = lowerBand > previousLowerBand || previousClose < previousLowerBand
                    ? lowerBand : previousLowerBand;
                double currentClose = (double)candles[candleIndex].Close;
                isUpwardTrend = isUpwardTrend ? currentClose >= lowerBand : currentClose > upperBand;
            }
            superTrend[candleIndex] = isUpwardTrend ? lowerBand : upperBand;
            previousUpperBand = upperBand;
            previousLowerBand = lowerBand;
        }
        return superTrend;
    }


    private static double[] CalculateAverageTrueRange(Candle[] candles, int period)
    {
        double[] averageTrueRange = CreateUnavailableSeries(candles.Length);
        double initialRangeSum = 0;
        for (int candleIndex = 0; candleIndex < candles.Length; candleIndex++)
        {
            double trueRange = (double)(candles[candleIndex].High - candles[candleIndex].Low);
            if (candleIndex > 0)
            {
                double previousClose = (double)candles[candleIndex - 1].Close;
                trueRange = Math.Max(trueRange, Math.Max(
                    Math.Abs((double)candles[candleIndex].High - previousClose),
                    Math.Abs((double)candles[candleIndex].Low - previousClose)));
            }
            // Include the first candle's high-low range in the initial period.
            if (candleIndex < period)
            {
                initialRangeSum += trueRange;
                if (candleIndex == period - 1)
                {
                    averageTrueRange[candleIndex] = initialRangeSum / period;
                }
            }
            else
            {
                averageTrueRange[candleIndex] = (averageTrueRange[candleIndex - 1] * (period - 1) + trueRange) / period;
            }
        }
        return averageTrueRange;
    }

}
