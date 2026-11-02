using AlgoTrading.DataAccess.Historical;
using AlgoTrading.DataAccess.Calendar;
using AlgoTrading.DataAccess.Etl;
using AlgoTrading.DataAccess.Repositories;
using AlgoTrading.Models;
using AlgoTrading.Models.MarketData.Ingestion;
using System.Net.Http;

namespace AlgoTrading.Services;

public interface IHistoricalDataWorkflow
{
    Task<HistoricalBackfillReport> FetchAsync(IReadOnlyCollection<long> instrumentTokens,
        IReadOnlyCollection<string> intervals, DateTime from, DateTime to, Action<string>? progress = null,
        IProgress<HistoricalBackfillProgress>? workProgress = null,
        CancellationToken cancellationToken = default);
}

public sealed class HistoricalDataWorkflow : IHistoricalDataWorkflow
{
    private readonly HttpClient _httpClient;
    private readonly SqlHistoricalIngestionRepository _repository;
    private readonly IAccessTokenProvider _accessTokenProvider;

    private readonly IInstrumentRegistry _registry;
    private readonly WarmupEtlService _warmup;
    private readonly ITradingCalendarRepository _calendars;
    private readonly TradingCalendarSelection _calendarSelection;
    private readonly string _resourcesFolder;
    private readonly SemaphoreSlim _fetchGate = new(1, 1);

    public HistoricalDataWorkflow(SqlHistoricalIngestionRepository repository, HttpClient httpClient,
        IAccessTokenProvider accessTokenProvider, IInstrumentRegistry registry, WarmupEtlService warmup,
        ITradingCalendarRepository calendars, TradingCalendarSelection calendarSelection, string resourcesFolder)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _accessTokenProvider = accessTokenProvider ?? throw new ArgumentNullException(nameof(accessTokenProvider));
        _registry = registry;
        _warmup = warmup;
        _calendars = calendars;
        _calendarSelection = calendarSelection;
        _resourcesFolder = resourcesFolder;
    }

    public async Task<HistoricalBackfillReport> FetchAsync(IReadOnlyCollection<long> instrumentTokens,
        IReadOnlyCollection<string> intervals, DateTime from, DateTime to, Action<string>? progress = null,
        IProgress<HistoricalBackfillProgress>? workProgress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instrumentTokens);
        if (instrumentTokens.Count == 0) throw new InvalidOperationException("No instruments were selected.");
        ArgumentNullException.ThrowIfNull(intervals);
        if (intervals.Count == 0) throw new InvalidOperationException("No historical intervals were selected.");
        if (from.Kind != DateTimeKind.Unspecified || to.Kind != DateTimeKind.Unspecified)
            throw new ArgumentException("Historical dates must be unspecified IST wall times.");
        if (from > to) throw new ArgumentException("From must not be later than To.", nameof(from));
        string? accessToken = _accessTokenProvider.AccessToken;
        if (string.IsNullOrWhiteSpace(accessToken))
            throw new InvalidOperationException("An access token is required for historical data.");

        int[] tokens = instrumentTokens.Distinct().Select(token => checked((int)token)).ToArray();
        var model = new HistoricalDataModel(_httpClient, accessToken, _repository)
        {
            Progress = progress,
            BackfillProgress = workProgress
        };
        string[] streams = intervals.Distinct().Select(interval => interval switch
        {
            HistoryCandleInterval.OneMinute => HistoricalStreamTypes.IntradayOneMinute,
            HistoryCandleInterval.FiveMinutes => HistoricalStreamTypes.IntradayFiveMinute,
            HistoryCandleInterval.FifteenMinutes => HistoricalStreamTypes.IntradayFifteenMinute,
            HistoryCandleInterval.Hour => HistoricalStreamTypes.IntradayOneHour,
            HistoryCandleInterval.Daily => HistoricalStreamTypes.DailyOneDay,
            _ => throw new ArgumentException("Unsupported historical interval: " + interval + ".", nameof(intervals))
        }).ToArray();
        await _fetchGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            int[] missing = tokens.Where(token => _registry.GetByToken(token) == null).ToArray();
            string scannerFolder = System.IO.Path.Combine(_resourcesFolder, "Screener");
            var dependencies = new ScannerDependencyRegistry();
            string[] warmupStreams = Array.Empty<string>();
            if (missing.Length > 0)
            {
                dependencies.LoadFromDirectory(scannerFolder);
                warmupStreams = dependencies.GetActiveTimeframes().Select(tf => tf switch
                {
                    "1" => HistoricalStreamTypes.IntradayOneMinute,
                    "5" => HistoricalStreamTypes.IntradayFiveMinute,
                    "15" => HistoricalStreamTypes.IntradayFifteenMinute,
                    "60" => HistoricalStreamTypes.IntradayOneHour,
                    "D" => HistoricalStreamTypes.DailyOneDay,
                    _ => throw new NotSupportedException($"Historical warmup does not support scanner timeframe {tf}.")
                }).ToArray();
                if (warmupStreams.Length == 0)
                    throw new InvalidOperationException("No scanner timeframes were found; warmup cannot start.");
            }

            var request = new HistoricalBackfillRequest(from, to, from, to, streams, model.BackfillBudget, true);
            var report = await model.GetHistoricalDataFromApiAsync(tokens, request, cancellationToken).ConfigureAwait(false);
            if (missing.Length == 0 || !report.IsComplete) return report;

            // Warmup reads the last year from SQL, independent of the UI's selected range/intervals.
            DateTime marketNow = MarketTimestamp.ToDatabase(DateTimeOffset.UtcNow);
            var warmupRequest = new HistoricalBackfillRequest(marketNow.Date.AddYears(-1), marketNow,
                marketNow.Date.AddYears(-1), marketNow, warmupStreams, model.BackfillBudget, true);
            progress?.Invoke("Fetching scanner-required history for new instruments.");
            var warmupReport = await model.GetHistoricalDataFromApiAsync(missing, warmupRequest, cancellationToken)
                .ConfigureAwait(false);
            var combined = new HistoricalBackfillReport(report.RequestedChunks + warmupReport.RequestedChunks,
                report.CompletedChunks + warmupReport.CompletedChunks, report.StoredChunks + warmupReport.StoredChunks,
                warmupReport.BudgetExhausted, new List<string>());
            if (!warmupReport.IsComplete) return combined;

            var calendar = await _calendars.LoadAsync(_calendarSelection.ExchangeCode, _calendarSelection.SegmentCode,
                _calendarSelection.Revision, DateTimeOffset.UtcNow, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("The selected trading calendar is unavailable for warmup.");
            progress?.Invoke("Warming up new instruments and saving the snapshot pair.");
            var additions = await _warmup.RunWarmupEtlAsync(scannerFolder, _resourcesFolder, cancellationToken,
                calendar, missing).ConfigureAwait(false);
            // Publish only after every required timeframe/indicator and both files succeed.
            _registry.AddMissing(additions);
            progress?.Invoke("New instruments warmed up and added to the snapshot.");
            return combined;
        }
        finally
        {
            _fetchGate.Release();
        }
    }
}
