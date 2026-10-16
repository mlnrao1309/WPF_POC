using AlgoTrading.Dtos;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace AlgoTrading.Services;

public interface IMarketWatchApiService
{
    Task<MarketWatchApiData?> GetMarketWatchAsync(int watchId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MarketWatchApiData>> GetAllMarketWatchesAsync(CancellationToken cancellationToken = default);
}

public sealed class MarketWatchApiService : IMarketWatchApiService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly IAccessTokenProvider _accessTokenProvider;

    public MarketWatchApiService(HttpClient httpClient, IAccessTokenProvider accessTokenProvider)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _accessTokenProvider = accessTokenProvider ?? throw new ArgumentNullException(nameof(accessTokenProvider));
    }

    public async Task<MarketWatchApiData?> GetMarketWatchAsync(int watchId, CancellationToken cancellationToken = default)
    {
        if (watchId is < 1 or > 7) throw new ArgumentOutOfRangeException(nameof(watchId));
        string token = _accessTokenProvider.AccessToken
            ?? throw new InvalidOperationException("An access token is required to synchronize MarketWatch.");
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"https://kite.zerodha.com/api/marketwatch/{watchId}/items?uid=marketwatch");
        request.Headers.TryAddWithoutValidation("Authorization", "enctoken " + token);
        request.Headers.TryAddWithoutValidation("Cookie", "enctoken=" + token);
        request.Headers.TryAddWithoutValidation("X-Kite-Version", "3");
        request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0");
        request.Headers.Referrer = new Uri("https://kite.zerodha.com/");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using HttpResponseMessage response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            string details = await response.Content.ReadAsStringAsync(cancellationToken);
            if (details.Length > 1024) details = details[..1024];
            throw new HttpRequestException(
                $"MarketWatch {watchId} request failed with HTTP {(int)response.StatusCode} ({response.ReasonPhrase}): {details}",
                null,
                response.StatusCode);
        }
        await using Stream content = await response.Content.ReadAsStreamAsync(cancellationToken);
        MarketWatchApiResponse? payload = await JsonSerializer.DeserializeAsync<MarketWatchApiResponse>(content, JsonOptions, cancellationToken);
        if (payload?.Data is null) throw new InvalidDataException($"MarketWatch {watchId} returned no data.");
        if (payload.Data.Id != watchId) throw new InvalidDataException($"MarketWatch {watchId} returned mismatched id {payload.Data.Id}.");
        payload.Data.Items ??= [];
        return payload.Data;
    }

    public async Task<IReadOnlyList<MarketWatchApiData>> GetAllMarketWatchesAsync(CancellationToken cancellationToken = default)
    {
        List<MarketWatchApiData> watches = new(7);
        for (int watchId = 1; watchId <= 7; watchId++)
        {
            try
            {
                MarketWatchApiData? watch = await GetMarketWatchAsync(watchId, cancellationToken);
                if (watch is not null) watches.Add(watch);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                Trace.TraceError("Unable to retrieve MarketWatch {0}. No reconciliation was performed. {1}", watchId, exception.Message);
            }
        }
        return watches;
    }
}
