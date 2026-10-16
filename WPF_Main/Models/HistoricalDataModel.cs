namespace AlgoTrading.Models
{
    using AlgoTrading.DataAccess.Historical;
    using AlgoTrading.Models.MarketData.Ingestion;
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Net.Http;

    public class HistoryCandleInterval
    {
        public const string OneMinute = "minute";
        public const string ThreeMinutes = "3minute";
        public const string FiveMinutes = "5minute";
        public const string TenMinutes = "10minute";
        public const string FifteenMinutes = "15minute";
        public const string ThirtyMinutes = "30minute";
        public const string Hour = "hour";
        public const string Daily = "day";

        public static readonly string[] AllIntervals = new string[]
        {
            OneMinute,
            FiveMinutes,
            FifteenMinutes,
            Hour,
            Daily
        };
    }

    //public sealed class InstrumentHistory
    //{
    //    public int InstrumentToken { get; init; }

    //    public Dictionary<string, List<Candle>> Intervals { get; } = new Dictionary<string, List<Candle>>();
    //}

    /// <summary>
    /// Historical (peace-time) ingestion of Zerodha Kite candles. The 2-hour default budget keeps the
    /// backfill deliberately slow so live throughput is never competing with historical requests.
    /// </summary>
    public sealed class HistoricalDataModel : IHistoricalChunkSource
    {
        private const int MaximumAttempts = 4;
        private const int TooManyRequestsStatus = 429;
        private static readonly TimeSpan RequestDelay = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan DefaultBackfillBudget = TimeSpan.FromHours(2);
        private const string BaseUrl = "https://kite.zerodha.com/oms/instruments/historical/";

        private readonly HttpClient httpClient;
        private readonly SqlHistoricalIngestionRepository repository;
        private readonly SemaphoreSlim requestGate = new SemaphoreSlim(1, 1);
        private DateTime nextRequestUtc;
        private string? accessToken;
        private readonly Dictionary<int, List<Candle>> historicalData;

        public HistoricalDataModel(HttpClient httpClient, string? accessToken,
            SqlHistoricalIngestionRepository repository)
        {
            if (httpClient == null)
            {
                throw new ArgumentNullException(nameof(httpClient));
            }

            if (repository == null)
            {
                throw new ArgumentNullException(nameof(repository));
            }

            this.httpClient = httpClient;
            this.repository = repository;
            this.accessToken = accessToken;
            this.historicalData = new Dictionary<int, List<Candle>>();
            this.BackfillBudget = DefaultBackfillBudget;
        }

        public Dictionary<int, List<Candle>> HistoricalData
        {
            get
            {
                return this.historicalData;
            }
        }

        public string? AccessToken
        {
            get
            {
                return Volatile.Read(ref this.accessToken);
            }
            set
            {
                UpdateAccessToken(value);
            }
        }

        public void UpdateAccessToken(string? value) => Interlocked.Exchange(ref this.accessToken, value);

        /// <summary>Peace-time window in which historical chunks may be fetched. Exhausting it is not an error.</summary>
        public TimeSpan BackfillBudget { get; set; }

        public Action<string>? Progress { get; set; }

        public IProgress<HistoricalBackfillProgress>? BackfillProgress { get; set; }

        /// <summary>
        /// Compatibility entry point for callers that specify a relative range. New background callers should use
        /// the explicit-range overload so configured source coverage, rather than an assumed current date, controls
        /// the requested range.
        /// </summary>
        public async Task<HistoricalBackfillReport> GetHistoricalDataFromApiAsync(int[] instrumentTokens, string[]? intervals,
            int years = 5, CancellationToken cancellationToken = default)
        {
            if (instrumentTokens == null || instrumentTokens.Length == 0)
            {
                throw new ArgumentException("Instrument token array cannot be null or empty.", nameof(instrumentTokens));
            }

            if (years <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(years), "Years must be greater than zero.");
            }

            DateTime marketNow = MarketTimestamp.ToDatabase(MarketTimestamp.FromDateTime(DateTime.UtcNow));
            TimeSpan budget = this.BackfillBudget > TimeSpan.Zero ? this.BackfillBudget : DefaultBackfillBudget;
            IReadOnlyList<string> streamTypes = ConvertIntervalsToSourceStreams(intervals);
            HistoricalBackfillRequest request = new HistoricalBackfillRequest(marketNow.AddYears(-years), marketNow,
                marketNow.AddYears(-years), marketNow, streamTypes, budget, false);
            return await this.GetHistoricalDataFromApiAsync(instrumentTokens, request, cancellationToken);
        }

        /// <summary>Runs a resumable backfill over the intersection of requested and configured source coverage.</summary>
        public async Task<HistoricalBackfillReport> GetHistoricalDataFromApiAsync(int[] instrumentTokens,
            HistoricalBackfillRequest request, CancellationToken cancellationToken = default)
        {
            if (instrumentTokens == null || instrumentTokens.Length == 0)
            {
                throw new ArgumentException("Instrument token array cannot be null or empty.", nameof(instrumentTokens));
            }

            HistoricalBackfillCoordinator coordinator = new HistoricalBackfillCoordinator(this, this.repository,
                new SystemHistoricalBackfillClock());
            coordinator.Progress = this.Progress;
            coordinator.WorkProgress = this.BackfillProgress;
            return await coordinator.RunAsync(instrumentTokens, request, cancellationToken).ConfigureAwait(false);
        }

        public async Task ImportCompletedCandleAsync(IReadOnlyList<int> instrumentTokens, string interval,
            DateTime candleStart, DateTime candleEnd, CancellationToken cancellationToken = default,
            int? chunkSizeDays = null)
        {
            if (instrumentTokens == null || instrumentTokens.Count == 0)
            {
                throw new ArgumentException("Instrument tokens cannot be null or empty.", nameof(instrumentTokens));
            }

            if (chunkSizeDays <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(chunkSizeDays), "Chunk size must be greater than zero.");
            }

            string streamType = ConvertPollingInterval(interval);
            int effectiveChunkSizeDays = chunkSizeDays ?? HistoricalStreamTypes.GetChunkDays(streamType);
            for (int index = 0; index < instrumentTokens.Count; index++)
            {
                DateTime currentStart = candleStart;
                while (currentStart < candleEnd)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    DateTime currentEnd = currentStart.AddDays(effectiveChunkSizeDays);
                    if (currentEnd > candleEnd)
                    {
                        currentEnd = candleEnd;
                    }

                    var request = new HistoricalChunkRequest(instrumentTokens[index], streamType, currentStart.Date,
                        currentEnd.AddTicks(-1).Date, currentStart, currentEnd);
                    string payload = await this.DownloadChunkAsync(request, cancellationToken);
                    await this.repository.PersistChunkAsync(request, payload, cancellationToken);
                    currentStart = currentEnd;
                }
            }
        }

        private static TimeSpan GetRetryDelay(HttpResponseMessage response, int attempt)
        {
            if (response.Headers.RetryAfter != null)
            {
                if (response.Headers.RetryAfter.Delta.HasValue)
                {
                    return response.Headers.RetryAfter.Delta.Value;
                }

                if (response.Headers.RetryAfter.Date.HasValue)
                {
                    TimeSpan difference = response.Headers.RetryAfter.Date.Value - DateTimeOffset.UtcNow;
                    if (difference > TimeSpan.Zero)
                    {
                        return difference;
                    }
                }
            }

            return TimeSpan.FromSeconds(Math.Pow(2.0, attempt));
        }

        public async Task<string> DownloadChunkAsync(HistoricalChunkRequest chunk, CancellationToken cancellationToken)
        {
            string url = BaseUrl + chunk.InstrumentToken.ToString(CultureInfo.InvariantCulture) + "/"
                + chunk.ProviderInterval + "?from="
                + chunk.RangeStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                + "&to=" + chunk.RangeEnd.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                + "&oi=1";

            Exception? lastFailure = null;
            for (int attempt = 1; attempt <= MaximumAttempts; attempt++)
            {
                await this.WaitForRequestSlotAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, url))
                    {
                        string? currentAccessToken = AccessToken;
                        if (!string.IsNullOrWhiteSpace(currentAccessToken))
                        {
                            request.Headers.TryAddWithoutValidation("Authorization", "enctoken " + currentAccessToken);
                        }

                        using (HttpResponseMessage response = await this.httpClient.SendAsync(request,
                            HttpCompletionOption.ResponseContentRead, cancellationToken).ConfigureAwait(false))
                        {
                            if (response.IsSuccessStatusCode)
                            {
                                return await response.Content.ReadAsStringAsync(cancellationToken)
                                    .ConfigureAwait(false);
                            }

                            int statusCode = (int)response.StatusCode;
                            bool retryable = statusCode == TooManyRequestsStatus || statusCode >= 500;
                            if (!retryable || attempt == MaximumAttempts)
                            {
                                throw new HttpRequestException("Kite rejected the historical request with " + statusCode
                                    + " (" + response.ReasonPhrase + ") for " + url, null, response.StatusCode);
                            }

                            TimeSpan delay = GetRetryDelay(response, attempt);
                            this.ReportProgress("Kite returned " + statusCode + "; retrying in " + delay + ". " + url);
                            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                        }
                    }
                }
                catch (HttpRequestException exception) when (attempt < MaximumAttempts && !exception.StatusCode.HasValue)
                {
                    lastFailure = exception;
                    TimeSpan backoff = TimeSpan.FromSeconds(Math.Pow(2.0, attempt));
                    this.ReportProgress("Historical request failed; retrying in " + backoff + ". " + url);
                    await Task.Delay(backoff, cancellationToken).ConfigureAwait(false);
                }
            }

            throw new HttpRequestException("Historical request failed after " + MaximumAttempts + " attempts: " + url, lastFailure);
        }

        private async Task WaitForRequestSlotAsync(CancellationToken cancellationToken)
        {
            await this.requestGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                TimeSpan remaining = this.nextRequestUtc - DateTime.UtcNow;
                if (remaining > TimeSpan.Zero)
                {
                    await Task.Delay(remaining, cancellationToken).ConfigureAwait(false);
                }

                this.nextRequestUtc = DateTime.UtcNow + RequestDelay;
            }
            finally
            {
                this.requestGate.Release();
            }
        }

        private static IReadOnlyList<string> ConvertIntervalsToSourceStreams(string[]? intervals)
        {
            string[] requestedIntervals = intervals ?? HistoryCandleInterval.AllIntervals;
            List<string> streamTypes = new List<string>();
            for (int index = 0; index < requestedIntervals.Length; index++)
            {
                string interval = requestedIntervals[index];
                if (interval == HistoryCandleInterval.FiveMinutes)
                {
                    streamTypes.Add(HistoricalStreamTypes.IntradayFiveMinute);
                }
                else if (interval == HistoryCandleInterval.OneMinute)
                {
                    streamTypes.Add(HistoricalStreamTypes.IntradayOneMinute);
                }
                else if (interval == HistoryCandleInterval.FifteenMinutes)
                {
                    streamTypes.Add(HistoricalStreamTypes.IntradayFifteenMinute);
                }
                else if (interval == HistoryCandleInterval.Hour)
                {
                    streamTypes.Add(HistoricalStreamTypes.IntradayOneHour);
                }
                else if (interval == HistoryCandleInterval.Daily)
                {
                    streamTypes.Add(HistoricalStreamTypes.DailyOneDay);
                }
                else
                {
                    throw new ArgumentException("Unsupported historical interval: " + interval + ".",
                        nameof(intervals));
                }
            }

            return streamTypes.AsReadOnly();
        }

        private static string ConvertPollingInterval(string interval)
        {
            return interval switch
            {
                HistoryCandleInterval.OneMinute => HistoricalStreamTypes.IntradayOneMinute,
                HistoryCandleInterval.FiveMinutes => HistoricalStreamTypes.IntradayFiveMinute,
                HistoryCandleInterval.FifteenMinutes => HistoricalStreamTypes.IntradayFifteenMinute,
                HistoryCandleInterval.Hour => HistoricalStreamTypes.IntradayOneHour,
                HistoryCandleInterval.Daily => HistoricalStreamTypes.DailyOneDay,
                _ => throw new ArgumentException("Unsupported polling interval: " + interval + ".", nameof(interval))
            };
        }

        private void ReportProgress(string message)
        {
            Action<string>? progress = this.Progress;
            if (progress != null)
            {
                progress(message);
            }
        }
    }
}
