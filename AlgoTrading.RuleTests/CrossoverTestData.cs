using AlgoTrading.Models;
using AlgoTrading.Models.Rules;

internal static class CrossoverTestData
{
    private static readonly DateTimeOffset origin = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    internal static DateTimeOffset Origin
    {
        get
        {
            return origin;
        }
    }

    internal static CompletedCandle[] CreateCandles(string timeframe, int intervalMinutes, params decimal[] prices)
    {
        CompletedCandle[] candles = new CompletedCandle[prices.Length];
        for (int candleIndex = 0; candleIndex < prices.Length; candleIndex++)
        {
            decimal price = prices[candleIndex];
            Candle candle = new Candle(1, timeframe, Origin.AddMinutes(candleIndex * intervalMinutes).UtcDateTime, price, price + 1, price - 1, price, 100);
            candles[candleIndex] = new CompletedCandle(candle, Origin.AddMinutes((candleIndex + 1) * intervalMinutes));
        }

        return candles;
    }

    internal static RuleMarketData CreateMarket(string timeframe, CompletedCandle[] candles)
    {
        Dictionary<string, IReadOnlyList<CompletedCandle>> series = new Dictionary<string, IReadOnlyList<CompletedCandle>>();
        series.Add(timeframe, candles);
        return new RuleMarketData(1, series);
    }

    internal static BoundRule CreateRule(ConditionDefinition condition)
    {
        return RuleBinder.Bind(new RuleDefinition("Crossover test", "15 minute", condition));
    }

    internal static ComparisonCondition CloseCrossesAbove(double threshold)
    {
        return new ComparisonCondition(new CandleValue("15 minute"), ComparisonOperator.CrossedAbove, new NumberValue(threshold));
    }
}


