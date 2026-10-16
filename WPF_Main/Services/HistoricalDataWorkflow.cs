using AlgoTrading.DataAccess.Historical;
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

    public HistoricalDataWorkflow(SqlHistoricalIngestionRepository repository, HttpClient httpClient,
        IAccessTokenProvider accessTokenProvider)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _accessTokenProvider = accessTokenProvider ?? throw new ArgumentNullException(nameof(accessTokenProvider));
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
        var request = new HistoricalBackfillRequest(from, to, from, to, streams, model.BackfillBudget, true);
        return await model.GetHistoricalDataFromApiAsync(tokens, request, cancellationToken).ConfigureAwait(false);
    }
}
