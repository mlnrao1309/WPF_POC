using AlgoTrading.DataAccess.Calendar;
using AlgoTrading.Models;
using System.Net;
using System.Text;

internal sealed class CalendarImportTests
{
    private int assertionCount;

    internal async Task RunAsync()
    {
        const string payload = """
            {"CM":[
              {"tradingDate":"26-Jan-2026","weekDay":"Monday","description":"Republic Day","morning_session":null,"evening_session":null,"Sr_no":1},
              {"tradingDate":"08-Nov-2026","weekDay":"Sunday","description":"Diwali Laxmi Pujan*","morning_session":null,"evening_session":null,"Sr_no":2}
            ]}
            """;
        Uri endpoint = new Uri("https://www.nseindia.com/api/holiday-master?type=trading");
        StubHttpHandler handler = new StubHttpHandler(payload);
        HttpClient httpClient = new HttpClient(handler);
        NseHolidaySourceClient client = new NseHolidaySourceClient(httpClient, endpoint);
        CalendarSourceDocument circular = new CalendarSourceDocument(Guid.NewGuid(), "NSE",
            new Uri("https://www.nseindia.com/resources/exchange-communication-circulars"),
            new DateTimeOffset(2026, 9, 22, 10, 0, 0, TimeSpan.Zero), "application/pdf", "official circular bytes");
        NseSpecialSession muhurat = new NseSpecialSession(new DateOnly(2026, 11, 8),
            new TimeOnly(18, 0), new TimeOnly(19, 0), "Muhurat trading", circular);
        NseCalendarImportRequest request = new NseCalendarImportRequest("CM", 2026, "NSE-CM-2026-v1",
            new TimeOnly(9, 15), new TimeOnly(15, 30), new[] { muhurat });
        RecordingRepository repository = new RecordingRepository();
        NseTradingCalendarImporter importer = new NseTradingCalendarImporter(client,
            new NseTradingCalendarParser(), repository);

        ExchangeCalendarSnapshot imported = await importer.ImportAsync(request);
        Assert(handler.CallCount == 1, "The official holiday endpoint must be downloaded exactly once.");
        Assert(repository.Events.SequenceEqual(new[] { "source", "source", "publish" }),
            "All original sources must be stored before the revision is published.");
        Assert(repository.Sources[0].Content == payload, "The original NSE response was not preserved byte-for-byte.");
        Assert(imported.Days.Count == 365, "A non-leap-year revision must contain one explicit row per date.");
        Assert(Find(imported, new DateOnly(2026, 1, 2)).OpensAt == new TimeOnly(9, 15),
            "An ordinary weekday did not receive regular session hours.");
        Assert(Find(imported, new DateOnly(2026, 1, 3)).OpensAt == null,
            "A weekend was not represented as an explicit closure.");
        Assert(Find(imported, new DateOnly(2026, 1, 26)).Reason.StartsWith("NSE holiday", StringComparison.Ordinal),
            "An NSE holiday was not represented as an explicit closure.");
        Assert(Find(imported, new DateOnly(2026, 11, 8)).OpensAt == new TimeOnly(18, 0),
            "An official special session must override both holiday and weekend closure rules.");

        repository.LoadResult = imported;
        ITradingCalendarProvider provider = new RepositoryTradingCalendarProvider(repository);
        ITradingSessionCalendar runtime = await provider.LoadAsync(
            new TradingCalendarSelection("NSE", "CM", "NSE-CM-2026-v1"), 265,
            new DateTimeOffset(2026, 12, 31, 0, 0, 0, TimeSpan.Zero));
        Assert(runtime is TradingSessionCalendar materialized && materialized.Revision == "NSE-CM-2026-v1",
            "Runtime did not use the selected repository revision.");
        Assert(runtime.GetSession(265, new DateOnly(2026, 11, 8)).Session?.InstrumentToken == 265,
            "Repository rows were not materialized for the requested instrument.");
        Assert(repository.LastLoad == ("NSE", "CM", "NSE-CM-2026-v1"),
            "Runtime repository selection was changed or ignored.");

        VerifyMissingSpecialSessionRemainsClosed(client);
        Console.WriteLine("Passed " + assertionCount + " calendar importer/runtime assertions.");
    }

    private void VerifyMissingSpecialSessionRemainsClosed(NseHolidaySourceClient client)
    {
        CalendarSourceDocument source = new CalendarSourceDocument(Guid.NewGuid(), "NSE",
            new Uri("https://www.nseindia.com/api/holiday-master?type=trading"), DateTimeOffset.UtcNow,
            "application/json", "{\"CM\":[{\"tradingDate\":\"08-Nov-2026\",\"description\":\"Diwali*\"}]}");
        HolidaySourceReport report = client.Parse(source, "CM", 2026);
        ExchangeCalendarSnapshot snapshot = new NseTradingCalendarParser().Parse(report,
            new NseCalendarImportRequest("CM", 2026, "safe", new TimeOnly(9, 15), new TimeOnly(15, 30)));
        Assert(Find(snapshot, new DateOnly(2026, 11, 8)).OpensAt == null,
            "A marked special day without official times must remain closed.");
    }

    private static ExchangeCalendarDay Find(ExchangeCalendarSnapshot snapshot, DateOnly date)
    {
        return snapshot.Days.Single(day => day.Date == date);
    }

    private void Assert(bool condition, string message)
    {
        assertionCount++;
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class StubHttpHandler : HttpMessageHandler
    {
        private readonly string payload;

        internal StubHttpHandler(string payload)
        {
            this.payload = payload;
        }

        internal int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            HttpResponseMessage response = new HttpResponseMessage(HttpStatusCode.OK);
            response.Content = new StringContent(payload, Encoding.UTF8, "application/json");
            return Task.FromResult(response);
        }
    }

    private sealed class RecordingRepository : ITradingCalendarRepository
    {
        internal List<string> Events { get; } = new List<string>();
        internal List<CalendarSourceDocument> Sources { get; } = new List<CalendarSourceDocument>();
        internal ExchangeCalendarSnapshot? LoadResult { get; set; }
        internal (string Exchange, string Segment, string Revision) LastLoad { get; private set; }

        public Task<bool> HasCalendarDaysAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(LoadResult?.Days.Count > 0);
        }

        public Task SaveSourceAsync(CalendarSourceDocument source, CancellationToken cancellationToken = default)
        {
            Events.Add("source");
            Sources.Add(source);
            return Task.CompletedTask;
        }

        public Task PublishAsync(ExchangeCalendarSnapshot calendar, CancellationToken cancellationToken = default)
        {
            Events.Add("publish");
            LoadResult = calendar;
            return Task.CompletedTask;
        }

        public Task<ExchangeCalendarSnapshot?> LoadAsync(string exchangeCode, string segmentCode, string revision,
            DateTimeOffset asOf, CancellationToken cancellationToken = default)
        {
            LastLoad = (exchangeCode, segmentCode, revision);
            return Task.FromResult(LoadResult);
        }
    }
}
