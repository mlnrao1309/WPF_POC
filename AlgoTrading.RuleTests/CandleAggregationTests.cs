using AlgoTrading.Models;
using AlgoTrading.Models.Rules;
using System.Globalization;

internal sealed class CandleAggregationTests
{
    private int assertionCount;
    private readonly DateTimeOffset sessionStart = DateTimeOffset.Parse("2026-09-24T09:15:00+05:30", CultureInfo.InvariantCulture);
    private readonly DateTimeOffset sessionClose = DateTimeOffset.Parse("2026-09-24T10:10:00+05:30", CultureInfo.InvariantCulture);

    internal void Run()
    {
        TradingSession session = new TradingSession(265, sessionStart, sessionClose);
        IReadOnlyList<CompletedCandle> source = ReadCandles("aggregation-five-minute.csv", "5minute", 5, session);
        VerifyTwentyMinuteAggregation(source, session);
        VerifyFortyFiveMinuteAggregation(source, session);
        VerifyBoundaryRejection(session);
        VerifyGapRejection(session);
        VerifyCalendarRejection(session);
        Console.WriteLine("Passed " + assertionCount + " candle aggregation assertions.");
    }

    private void Assert(bool condition, string message)
    {
        assertionCount++;
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private IReadOnlyList<CompletedCandle> ReadCandles(string fileName, string timeframe, int minutes, TradingSession session)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName);
        string[] lines = File.ReadAllLines(path);
        List<CompletedCandle> result = new List<CompletedCandle>();
        for (int index = 1; index < lines.Length; index++)
        {
            string[] cells = lines[index].Split(',');
            DateTime sourceStart = DateTime.Parse(cells[0], CultureInfo.InvariantCulture, DateTimeStyles.None);
            DateTimeOffset open = MarketTimestamp.FromDatabase(sourceStart);
            DateTimeOffset close = open.AddMinutes(minutes);
            if (close > session.ClosedAt)
            {
                close = session.ClosedAt;
            }
            Candle candle = new Candle(265, timeframe, sourceStart,
                decimal.Parse(cells[1], CultureInfo.InvariantCulture), decimal.Parse(cells[2], CultureInfo.InvariantCulture),
                decimal.Parse(cells[3], CultureInfo.InvariantCulture), decimal.Parse(cells[4], CultureInfo.InvariantCulture),
                long.Parse(cells[5], CultureInfo.InvariantCulture));
            result.Add(new CompletedCandle(candle, close));
        }
        return result.AsReadOnly();
    }

    private void VerifyTwentyMinuteAggregation(IReadOnlyList<CompletedCandle> source, TradingSession session)
    {
        TimeframeNormalizer normalizer = new TimeframeNormalizer();
        CandleAggregationRequest request = new CandleAggregationRequest(normalizer.Normalize("20 minutes"), session);
        CandleAggregator aggregator = new CandleAggregator(new IntradayCandleClock());
        IReadOnlyList<CompletedCandle> result = aggregator.Aggregate(source, request);
        Assert(result.Count == 3, "Twenty-minute aggregation must produce three intervals.");
        Assert(result[0].Candle.Open == 100 && result[0].Candle.High == 105 && result[0].Candle.Low == 99
            && result[0].Candle.Close == 104 && result[0].Candle.Volume == 100, "First twenty-minute OHLCV is wrong.");
        Assert(result[0].Candle.OpenedAt == session.OpenedAt && result[0].ClosedAt == session.OpenedAt.AddMinutes(20), "First boundary is wrong.");
        Assert(result[1].Candle.Open == 104 && result[1].Candle.High == 109 && result[1].Candle.Low == 103
            && result[1].Candle.Close == 108 && result[1].Candle.Volume == 260, "Second twenty-minute OHLCV is wrong.");
        Assert(result[2].Candle.Open == 108 && result[2].Candle.High == 112 && result[2].Candle.Low == 107
            && result[2].Candle.Close == 111 && result[2].Candle.Volume == 300, "Short final OHLCV is wrong.");
        Assert(result[2].ClosedAt == session.ClosedAt, "Final aggregate must end at session close.");
    }

    private void VerifyFortyFiveMinuteAggregation(IReadOnlyList<CompletedCandle> source, TradingSession session)
    {
        TimeframeNormalizer normalizer = new TimeframeNormalizer();
        CandleAggregationRequest request = new CandleAggregationRequest(normalizer.Normalize("45m"), session);
        IReadOnlyList<CompletedCandle> result = new CandleAggregator(new IntradayCandleClock()).Aggregate(source, request);
        Assert(result.Count == 2, "Forty-five-minute aggregation must produce two intervals.");
        Assert(result[0].ClosedAt == session.OpenedAt.AddMinutes(45) && result[0].Candle.Volume == 450, "Full forty-five-minute interval is wrong.");
        Assert(result[1].Candle.Volume == 210 && result[1].ClosedAt == session.ClosedAt, "Short final forty-five-minute interval is wrong.");
    }

    private void VerifyBoundaryRejection(TradingSession session)
    {
        IReadOnlyList<CompletedCandle> source = ReadCandles("aggregation-fifteen-minute.csv", "15minute", 15, session);
        CandleAggregationRequest request = new CandleAggregationRequest(new TimeframeNormalizer().Normalize("20m"), session);
        bool rejected = false;
        try
        {
            new CandleAggregator(new IntradayCandleClock()).Aggregate(source, request);
        }
        catch (InvalidOperationException)
        {
            rejected = true;
        }
        Assert(rejected, "A source candle crossing a twenty-minute boundary must be rejected.");
    }

    private void VerifyGapRejection(TradingSession session)
    {
        List<CompletedCandle> source = new List<CompletedCandle>(ReadCandles("aggregation-five-minute.csv", "5minute", 5, session));
        source.RemoveAt(2);
        CandleAggregationRequest request = new CandleAggregationRequest(new TimeframeNormalizer().Normalize("20m"), session);
        bool rejected = false;
        try
        {
            new CandleAggregator(new IntradayCandleClock()).Aggregate(source, request);
        }
        catch (InvalidOperationException)
        {
            rejected = true;
        }
        Assert(rejected, "Missing source intervals must not become a partial aggregate.");
    }

    private void VerifyCalendarRejection(TradingSession session)
    {
        bool rejected = false;
        try
        {
            TimeframeDefinition weekly = new TimeframeNormalizer().Normalize("W");
            new CandleAggregationRequest(weekly, session);
        }
        catch (ArgumentException)
        {
            rejected = true;
        }
        Assert(rejected, "Calendar periods must not enter intraday aggregation.");
    }
}
