using AlgoTrading.Screener.Storage;

internal sealed class ScreenerExecutionPlanTests
{
    private int assertions;

    public async Task RunAsync()
    {
        string folder = Path.Combine(AppContext.BaseDirectory, "sandbox-build-plans", Guid.NewGuid().ToString("N"));
        var builder = new ScreenerExecutionPlanBuilder();
        var store = new ScreenerPlanStore(folder);
        const string expression = "from s in Instruments where SMA(s.Close, 200)[1] > 0 select s.Symbol, s.Time, s.Close orderby s.Time desc";
        ScreenerExecutionPlan plan = builder.Build("SMA 200", expression, "15");

        Assert(plan.History.OverallRequiredCandles == 201, "Overall candle count must include the offset.");
        Assert(plan.RequiredFields.Contains("Close") && plan.RequiredFields.Contains("Time"), "Referenced fields must be persisted.");
        Assert(plan.Indicators.Single().RequiredInputCandles == 201, "Indicator candle count must include the offset.");
        Assert(plan.SqlRetrieval.CommandText.Contains("ROW_NUMBER()", StringComparison.Ordinal), "Finite plans must limit rows in SQL.");
        Assert(!plan.SqlRetrieval.CommandText.Contains("SELECT *", StringComparison.Ordinal), "SQL plans must project only required columns.");
        Assert(plan.SqlRetrieval.Parameters.Any(x => x.Name == "@RequiredBars"), "RequiredBars must be parameterized.");

        string path = await store.SaveAsync(plan, false);
        Assert(File.Exists(path), "Named plan was not created.");
        string json = await File.ReadAllTextAsync(path);
        Assert(json.Contains("\"overallRequiredCandles\": 201", StringComparison.Ordinal), "Persisted candle count is missing.");
        await store.SaveAsync(plan, false);

        ScreenerExecutionPlan changed = builder.Build("SMA 200", expression.Replace("200", "50", StringComparison.Ordinal), "15");
        await AssertThrowsAsync<ScreenerPlanCollisionException>(() => store.SaveAsync(changed, false));
        Assert(changed.ExpressionFingerprint != plan.ExpressionFingerprint, "Expression changes must invalidate a plan.");
        Assert(builder.Build("EMA", "from s in Instruments select EMA(s.Close, 20)", "15").History.OverallRequiredCandles is null,
            "Recursive indicators must not receive an unsafe row limit.");
        ScreenerExecutionPlan nested = builder.Build("Nested", "from s in Instruments let fast = SMA(s.Close, 5) select SMA(fast, 20)", "15");
        Assert(nested.Indicators.Last().Dependencies.Contains("SMA(s.Close, 5)"), "Nested let dependencies must be persisted.");
        ScreenerExecutionPlan multiInput = builder.Build("SuperTrend", "from s in Instruments select SUPERTREND(s.High, s.Low, s.Close, 10, 3)", "15");
        Assert(multiInput.Indicators.Single().Inputs.SequenceEqual(new[] { "High", "Low", "Close" }), "Multi-series inputs must be distinguished from parameters.");
        Assert(multiInput.Indicators.Single().Parameters.SequenceEqual(new[] { "10", "3" }), "Indicator parameters must be persisted separately.");
        AssertThrows<ArgumentException>(() => builder.Build("../bad", expression, "15"));
        AssertThrows<ArgumentException>(() => builder.Build("CON", expression, "15"));

        Console.WriteLine("Passed " + assertions + " Screener execution-plan assertions.");
        Console.WriteLine("RepresentativePlan=" + path);
    }

    private async Task AssertThrowsAsync<T>(Func<Task> action) where T : Exception
    {
        try { await action(); }
        catch (T) { assertions++; return; }
        throw new InvalidOperationException("Expected " + typeof(T).Name + ".");
    }

    private void AssertThrows<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { assertions++; return; }
        throw new InvalidOperationException("Expected " + typeof(T).Name + ".");
    }

    private void Assert(bool condition, string message)
    {
        assertions++;
        if (!condition) throw new InvalidOperationException(message);
    }
}
