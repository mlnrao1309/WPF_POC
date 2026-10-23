using AlgoTrading.Screener.Dsl;
using AlgoTrading.Screener.Storage;

internal sealed class ScreenerIndicatorTests
{
    public void Run()
    {
        var shorthand = Parser.Parse("from price in Instruments select SUPERTREND(10, 3)").Select.Single().Value;
        var explicitCall = Parser.Parse("from price in Instruments select SUPERTREND(price.High, price.Low, price.Close, 10, 3)").Select.Single().Value;
        var functions = Functions.Build();
        var cache = new IndicatorEvaluationCache();
        var cached = new Evaluator(functions, cache);
        var uncached = new Evaluator(functions);

        foreach (var (count, correction) in new[] { (30, false), (45, false), (45, true) })
        {
            var closes = Enumerable.Range(0, count)
                .Select(i => 100m + i % 13 - i % 7).ToArray();
            if (correction) closes[20] += 8m;
            var env = new EvalEnv(count);
            env.Bind("price", new SymbolValue("TEST", 1, new Dictionary<string, Series>
            {
                ["High"] = Series.Num(closes.Select(c => c + 3m)),
                ["Low"] = Series.Num(closes.Select(c => c - 2m)),
                ["Close"] = Series.Num(closes)
            }));
            var expected = uncached.EvalSeries(explicitCall, env);
            var actual = cached.EvalSeries(shorthand, env);
            if (expected.Last is null || !expected.Values.SequenceEqual(actual.Values))
                throw new Exception("SuperTrend shorthand must match explicit inputs on initial, appended and corrected history.");
        }

        var plan = new ScreenerExecutionPlanBuilder().Build("Implicit SuperTrend",
            "from price in Instruments select SUPERTREND(10, 3)", "15");
        if (!plan.Indicators.Single().Inputs.SequenceEqual(new[] { "High", "Low", "Close" })
            || !plan.Indicators.Single().Parameters.SequenceEqual(new[] { "10", "3" }))
            throw new Exception("SuperTrend shorthand must expose HLC dependencies in execution plans.");

        foreach (string args in new[] { "", "10", "10, 3, 2", "1, 2, 3, 4, 5, 6" })
        {
            try { Parser.Parse("from price in Instruments select SUPERTREND(" + args + ")"); }
            catch (FormatException exception) when (exception.Message.Contains("SUPERTREND requires", StringComparison.Ordinal))
            { continue; }
            throw new Exception("Malformed SuperTrend arguments must report the supported signatures.");
        }
        Console.WriteLine("Passed SuperTrend shorthand, cached calculation, append/correction, dependency and argument-validation checks.");
    }
}
