using System.Globalization;
using AlgoTrading.Models;
using AlgoTrading.Models.Rules;

internal sealed class DailyPivotTests
{
    private int assertionCount;

    internal void Run()
    {
        string[] lines = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Fixtures", "daily-pivot-cases.csv"));
        TradingSessionCalendar calendar = CreateCalendar();
        DailyPivotService service = new DailyPivotService();
        for (int index = 1; index < lines.Length; index++)
        {
            string[] cells = lines[index].Split(',');
            CompletedCandle source = Daily(21, Parse(cells[3]), Parse(cells[1]), Parse(cells[2]), Parse(cells[3]));
            CompletedCandle qualification = Daily(22, Parse(cells[4]), 120, 80, Parse(cells[5]));
            DailyPivotResult result = service.Calculate(source, qualification, calendar, qualification.ClosedAt);
            List<decimal> actual = Values(result.Pivots);
            for (int levelIndex = 0; levelIndex < actual.Count; levelIndex++)
            {
                Assert(actual[levelIndex] == Parse(cells[6 + levelIndex]), cells[0] + ": formula mismatch.");
            }
            bool ppr1 = bool.Parse(cells[17]);
            bool pps1 = bool.Parse(cells[18]);
            Assert(result.Ppr1 == ppr1 && result.Pps1 == pps1, "Qualification flags differ.");
            Assert((result.Origin != null) == (ppr1 || pps1), "Origin publication differs.");
            DailyPivotResult early = service.Calculate(source, qualification, calendar, qualification.ClosedAt.AddTicks(-1));
            Assert(early.Origin == null && early.Ppr1 == null && early.Pps1 == null, "Qualification leaked before close.");
            if (result.Origin != null)
            {
                Assert(result.Origin.Resistance == source.Candle.High && result.Origin.Support == source.Candle.Low, "Reference uses Q instead of S.");
                Assert(result.Origin.ActiveFrom == At(24, 9, 15), "Activation ignored the declared holiday.");
                Assert(!result.Origin.IsActiveAt(At(24, 9, 15).AddTicks(-1)), "Early critical access.");
                Assert(result.Origin.IsActiveAt(At(24, 9, 15).AddYears(50)), "Critical level expired.");
                DailyPivotResult repeat = service.Calculate(source, qualification, calendar, qualification.ClosedAt);
                Assert(repeat.Origin != null && repeat.Origin.Identity == result.Origin.Identity, "Origin identity is not repeatable.");
                Assert(result.Origin.Ppr1 == ppr1 && result.Origin.Pps1 == pps1, "Origin lost a qualification flag.");
            }
        }
        VerifyReferences(service, calendar);
        DailyPivotResult ordinary = service.CalculateOrdinary(Daily(21, 100, 110, 90, 100), calendar, At(22, 9, 15));
        Assert(ordinary.Pivots.Pivot == 100 && ordinary.Ppr1 == null && ordinary.Origin == null, "Ordinary pivots required future qualification OHLC.");
        VerifyMissingAndWrongSource(service, calendar);
        Console.WriteLine("Passed " + this.assertionCount + " daily pivot and reference assertions.");
    }

    private void VerifyReferences(DailyPivotService service, TradingSessionCalendar calendar)
    {
        CompletedCandle source = Daily(21, 100, 110, 90, 100);
        CompletedCandle qualification = Daily(22, 105, 112, 95, 108);
        CriticalLevelOrigin? origin = service.Calculate(source, qualification, calendar, qualification.ClosedAt).Origin;
        if (origin == null)
        {
            throw new InvalidOperationException("Fixture origin did not qualify.");
        }
        List<CompletedCandle> intraday = ReadIntraday();
        CriticalReferenceProjector projector = new CriticalReferenceProjector();
        TimeframeNormalizer normalizer = new TimeframeNormalizer();
        CriticalReferenceProjection fifteen = projector.Project(origin, normalizer.Normalize("15minute"), intraday, calendar, origin.ActiveFrom);
        CriticalReferenceProjection hourly = projector.Project(origin, normalizer.Normalize("1hour"), intraday, calendar, origin.ActiveFrom);
        Assert(fifteen.Start.Candle.High == 104 && fifteen.Start.Candle.Low == 99 && fifteen.Start.Candle.Close == 102, "15-minute start substituted daily prices.");
        Assert(hourly.Start.Candle.High == 110 && hourly.Start.Candle.Low == 90 && hourly.Start.Candle.Close == 103, "Hourly projection failed.");
        Assert(hourly.End.Candle.Open == 103 && hourly.End.Candle.High == 105 && hourly.End.Candle.Low == 98 && hourly.End.Candle.Close == 100, "End reference failed.");
        Assert(hourly.End.ClosedAt - hourly.End.Candle.OpenedAt == TimeSpan.FromMinutes(15), "Shortened final candle lost.");
        Assert(fifteen.Origin.Identity == hourly.Origin.Identity, "Projection changed global origin.");
        bool rejected = false;
        try
        {
            projector.Project(origin, normalizer.Normalize("15minute"), intraday, calendar, origin.ActiveFrom.AddTicks(-1));
        }
        catch (InvalidOperationException)
        {
            rejected = true;
        }
        Assert(rejected, "Projection became visible before activation.");
        intraday.RemoveAt(0);
        rejected = false;
        try
        {
            projector.Project(origin, normalizer.Normalize("15minute"), intraday, calendar, origin.ActiveFrom);
        }
        catch (InvalidOperationException)
        {
            rejected = true;
        }
        Assert(rejected, "Missing first candle was accepted.");
    }

    private void VerifyMissingAndWrongSource(DailyPivotService service, TradingSessionCalendar calendar)
    {
        bool rejected = false;
        try
        {
            service.Calculate(Daily(21, 100, 110, 90, 100), Daily(24, 105, 112, 95, 108), calendar, At(24, 10, 30));
        }
        catch (InvalidOperationException)
        {
            rejected = true;
        }
        Assert(rejected, "A missing qualification session was skipped.");
        rejected = false;
        try
        {
            service.Calculate(Daily(21, 100, 110, 90, 100), Daily(22, 105, 112, 95, 108), calendar, At(21, 10, 29));
        }
        catch (InvalidOperationException)
        {
            rejected = true;
        }
        Assert(rejected, "Incomplete source was used.");
    }

    private List<CompletedCandle> ReadIntraday()
    {
        string[] lines = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Fixtures", "daily-reference-candles.csv"));
        List<CompletedCandle> result = new List<CompletedCandle>();
        for (int index = 1; index < lines.Length; index++)
        {
            string[] cells = lines[index].Split(',');
            DateTime timestamp = DateTime.Parse(cells[0], CultureInfo.InvariantCulture);
            Candle candle = new Candle(265, "15minute", timestamp, Parse(cells[1]), Parse(cells[2]), Parse(cells[3]), Parse(cells[4]), long.Parse(cells[5], CultureInfo.InvariantCulture));
            result.Add(new CompletedCandle(candle, candle.OpenedAt.AddMinutes(15)));
        }
        return result;
    }

    private TradingSessionCalendar CreateCalendar()
    {
        List<TradingCalendarDay> days = new List<TradingCalendarDay>();
        for (int day = 21; day <= 24; day++)
        {
            TradingSession? session = null;
            if (day != 23)
            {
                session = new TradingSession(265, At(day, 9, 15), At(day, 10, 30));
            }
            days.Add(new TradingCalendarDay(new DateOnly(2026, 9, day), session));
        }
        return new TradingSessionCalendar(265, new DateOnly(2026, 9, 21), new DateOnly(2026, 9, 24), "synthetic-v1", days);
    }

    private CompletedCandle Daily(int day, decimal open, decimal high, decimal low, decimal close)
    {
        Candle candle = new Candle(265, "D", MarketTimestamp.ToDatabase(At(day, 9, 15)), open, high, low, close, 150);
        return new CompletedCandle(candle, At(day, 10, 30));
    }

    private DateTimeOffset At(int day, int hour, int minute)
    {
        return new DateTimeOffset(2026, 9, day, hour, minute, 0, TimeSpan.FromMinutes(330));
    }

    private decimal Parse(string value)
    {
        return decimal.Parse(value, CultureInfo.InvariantCulture);
    }

    private List<decimal> Values(SqlRangeExtensionPivots pivots)
    {
        List<decimal> values = new List<decimal>();
        values.Add(pivots.Pivot);
        values.Add(pivots.Resistance1);
        values.Add(pivots.Resistance2);
        values.Add(pivots.Resistance3);
        values.Add(pivots.Resistance4);
        values.Add(pivots.Resistance5);
        values.Add(pivots.Support1);
        values.Add(pivots.Support2);
        values.Add(pivots.Support3);
        values.Add(pivots.Support4);
        values.Add(pivots.Support5);
        return values;
    }

    private void Assert(bool condition, string message)
    {
        this.assertionCount++;
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
