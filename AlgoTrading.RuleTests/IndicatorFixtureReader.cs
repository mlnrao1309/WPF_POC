using System.Globalization;
using AlgoTrading.Models.Rules;

internal static class IndicatorFixtureReader
{
    internal static RuleMarketData ReadMarket(string name)
    {
        string[] lines = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));
        decimal[] prices = new decimal[lines.Length - 1];
        for (int index = 1; index < lines.Length; index++)
        {
            prices[index - 1] = decimal.Parse(lines[index], CultureInfo.InvariantCulture);
        }
        CompletedCandle[] candles = CrossoverTestData.CreateCandles("15 minute", 15, prices);
        return CrossoverTestData.CreateMarket("15 minute", candles);
    }
}
