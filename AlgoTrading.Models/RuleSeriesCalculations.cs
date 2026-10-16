namespace AlgoTrading.Models.Rules;

internal static class RuleSeriesCalculations
{
    internal static double[] Calculate(IndicatorValue indicator, double[] source)
    {
        double[] result = new double[source.Length];
        Array.Fill(result, double.NaN);
        int period = indicator.Length;
        if (indicator.Function == IndicatorFunction.RelativeStrengthIndex)
        {
            double averageGain = 0;
            double averageLoss = 0;
            int changeCount = 0;
            for (int sourceIndex = 1; sourceIndex < source.Length; sourceIndex++)
            {
                if (!double.IsFinite(source[sourceIndex]) || !double.IsFinite(source[sourceIndex - 1]))
                {
                    averageGain = averageLoss = 0;
                    changeCount = 0;
                    continue;
                }
                double change = source[sourceIndex] - source[sourceIndex - 1];
                double gain = Math.Max(change, 0);
                double loss = Math.Max(-change, 0);
                if (changeCount < period)
                {
                    averageGain += gain;
                    averageLoss += loss;
                    changeCount++;
                    if (changeCount < period) continue;
                    averageGain /= period;
                    averageLoss /= period;
                }
                else
                {
                    averageGain = (averageGain * (period - 1) + gain) / period;
                    averageLoss = (averageLoss * (period - 1) + loss) / period;
                }
                result[sourceIndex] = averageLoss == 0 ? averageGain == 0 ? 50 : 100 : 100 - 100 / (1 + averageGain / averageLoss);
            }
            return result;
        }
        if (indicator.Function == IndicatorFunction.ExponentialMovingAverage)
        {
            double previousAverage = double.NaN;
            double multiplier = 2.0 / (period + 1.0);
            for (int sourceIndex = 0; sourceIndex < source.Length; sourceIndex++)
            {
                double current = source[sourceIndex];
                if (!double.IsFinite(current)) { previousAverage = double.NaN; continue; }
                previousAverage = double.IsNaN(previousAverage) ? current : previousAverage + multiplier * (current - previousAverage);
                result[sourceIndex] = previousAverage;
            }
            return result;
        }
        double windowSum = 0;
        int unavailableCount = 0;
        for (int sourceIndex = 0; sourceIndex < source.Length; sourceIndex++)
        {
            if (double.IsFinite(source[sourceIndex])) windowSum += source[sourceIndex];
            else unavailableCount++;
            if (sourceIndex >= period)
            {
                if (double.IsFinite(source[sourceIndex - period])) windowSum -= source[sourceIndex - period];
                else unavailableCount--;
            }
            if (sourceIndex < period - 1 || unavailableCount != 0) continue;
            double average = windowSum / period;
            if (indicator.Function is IndicatorFunction.SimpleMovingAverage or IndicatorFunction.BollingerMiddleBand)
            {
                result[sourceIndex] = average;
                continue;
            }
            double squaredDeviationSum = 0;
            for (int windowIndex = sourceIndex - period + 1; windowIndex <= sourceIndex; windowIndex++)
            {
                double deviation = source[windowIndex] - average;
                squaredDeviationSum += deviation * deviation;
            }
            double bandWidth = indicator.Multiplier * Math.Sqrt(squaredDeviationSum / period);
            result[sourceIndex] = indicator.Function == IndicatorFunction.BollingerUpperBand ? average + bandWidth : average - bandWidth;
        }
        return result;
    }

    internal static double[] SuperTrend(CompletedCandle[] candles, int period, double multiplier)
    {
        double[] result = new double[candles.Length];
        Array.Fill(result, double.NaN);
        double initialRangeSum = 0;
        double averageTrueRange = 0;
        double previousUpperBand = 0;
        double previousLowerBand = 0;
        bool upwardTrend = true;
        for (int candleIndex = 0; candleIndex < candles.Length; candleIndex++)
        {
            Candle candle = candles[candleIndex].Candle;
            double trueRange = (double)(candle.High - candle.Low);
            if (candleIndex > 0)
            {
                double previousClose = (double)candles[candleIndex - 1].Candle.Close;
                trueRange = Math.Max(trueRange, Math.Max(Math.Abs((double)candle.High - previousClose), Math.Abs((double)candle.Low - previousClose)));
            }
            if (candleIndex < period)
            {
                initialRangeSum += trueRange;
                if (candleIndex < period - 1) continue;
                averageTrueRange = initialRangeSum / period;
            }
            else averageTrueRange = (averageTrueRange * (period - 1) + trueRange) / period;
            double midpoint = ((double)candle.High + (double)candle.Low) / 2;
            double upperBand = midpoint + multiplier * averageTrueRange;
            double lowerBand = midpoint - multiplier * averageTrueRange;
            if (candleIndex > period - 1)
            {
                double previousClose = (double)candles[candleIndex - 1].Candle.Close;
                upperBand = upperBand < previousUpperBand || previousClose > previousUpperBand ? upperBand : previousUpperBand;
                lowerBand = lowerBand > previousLowerBand || previousClose < previousLowerBand ? lowerBand : previousLowerBand;
                upwardTrend = upwardTrend ? (double)candle.Close >= lowerBand : (double)candle.Close > upperBand;
            }
            result[candleIndex] = upwardTrend ? lowerBand : upperBand;
            previousUpperBand = upperBand;
            previousLowerBand = lowerBand;
        }
        return result;
    }
}
