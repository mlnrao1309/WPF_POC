using AlgoTrading.Screener.Dsl;

internal sealed class ScreenerHistoryRequirementTests
{
    private int assertions;

    public void Run()
    {
        AssertBars("from s in Instruments where SMA(s.Close, 200) > 0 select s.Symbol", 200);
        AssertBars("from s in Instruments where SMA(s.Close, 200)[1] > 0 select s.Symbol", 201);
        AssertBars("from s in Instruments where SMA(s.Close, 50) > 0 select s.Symbol", 50);
        AssertBars("from s in Instruments where SMA(SMA(s.Close, 20), 5) > 0 select s.Symbol", 24);
        AssertBars("from s in Instruments where SMA(s.Close, 50) crossedabove SMA(s.Close, 200) select s.Symbol", 201);
        AssertBars("from s in Instruments let average = SMA(s.Close, 200) where average[1] > 0 select s.Symbol", 201);
        AssertUnbounded("from s in Instruments where EMA(s.Close, 20) > 0 select s.Symbol");
        AssertUnbounded("from s in Instruments where SMA(s.Close, 20) > 0 select s.Symbol per bar");
        AssertUnbounded("from s in Instruments where SMA(s.Close, 20)[s.Close] > 0 select s.Symbol");
        VerifySmaTailEquivalence();
        Console.WriteLine("Passed " + assertions + " Screener history-requirement assertions.");
    }

    private void AssertBars(string source, int expected)
    {
        QueryNode query = Parser.Parse(source);
        bool finite = QueryHistoryRequirements.TryGetFiniteBars(query, out int actual);
        Assert(finite && actual == expected,
            "Expected " + expected + " bars but received " + (finite ? actual : "unbounded") + ".");
    }

    private void AssertUnbounded(string source)
    {
        QueryNode query = Parser.Parse(source);
        Assert(!QueryHistoryRequirements.TryGetFiniteBars(query, out _),
            "Expression must retain existing date-range retrieval.");
    }

    private void VerifySmaTailEquivalence()
    {
        decimal?[] values = Enumerable.Range(1, 500).Select(x => (decimal?)x).ToArray();
        Series full = Series.Num(values);

        Assert(Indicators.Sma(full, 200).LastNum()
                == Indicators.Sma(Series.Num(values[^200..]), 200).LastNum(),
            "SMA(200) changed after bounded retrieval.");
        Assert(Indicators.Sma(full, 200).Shift(1).LastNum()
                == Indicators.Sma(Series.Num(values[^201..]), 200).Shift(1).LastNum(),
            "SMA(200)[1] changed after bounded retrieval.");

        Series fullNested = Indicators.Sma(Indicators.Sma(full, 20), 5);
        Series tailNested = Indicators.Sma(Indicators.Sma(Series.Num(values[^24..]), 20), 5);
        Assert(fullNested.LastNum() == tailNested.LastNum(),
            "Nested finite SMA changed after bounded retrieval.");

        decimal?[] insufficient = Enumerable.Range(1, 100).Select(x => (decimal?)x).ToArray();
        Assert(Indicators.Sma(Series.Num(insufficient), 200).LastNum() is null,
            "Insufficient history must not fabricate an SMA value.");
    }

    private void Assert(bool condition, string message)
    {
        assertions++;
        if (!condition) throw new InvalidOperationException(message);
    }
}
