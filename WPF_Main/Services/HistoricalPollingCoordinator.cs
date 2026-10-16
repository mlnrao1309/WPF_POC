using AlgoTrading.DataAccess.Historical;
using AlgoTrading.DataAccess.Calendar;
using AlgoTrading.Models;
using AlgoTrading.Models.MarketData.Ingestion;
using System.Diagnostics;
using System.Net.Http;

namespace AlgoTrading.Services;

public sealed class HistoricalPollingCoordinator : IHistoricalPollingCoordinator
{
    private static readonly TimeSpan MaximumGracePeriod = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan NonCasDailyReadyAt = new TimeSpan(16, 0, 0);
    private readonly HttpClient _httpClient;
    private readonly SqlHistoricalIngestionRepository _repository;
    private readonly IAccessTokenProvider _accessTokenProvider;
    private readonly ITradingCalendarProvider _calendarProvider;
    private readonly TradingCalendarSelection _calendarSelection;

    public HistoricalPollingCoordinator(SqlHistoricalIngestionRepository repository, HttpClient httpClient,
        IAccessTokenProvider accessTokenProvider, ITradingCalendarProvider calendarProvider,
        TradingCalendarSelection calendarSelection)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _accessTokenProvider = accessTokenProvider ?? throw new ArgumentNullException(nameof(accessTokenProvider));
        _calendarProvider = calendarProvider ?? throw new ArgumentNullException(nameof(calendarProvider));
        _calendarSelection = calendarSelection ?? throw new ArgumentNullException(nameof(calendarSelection));
    }

    public async Task RunAsync(IReadOnlyCollection<long> instrumentTokens, IReadOnlyCollection<string> intervals,
        IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instrumentTokens);
        ArgumentNullException.ThrowIfNull(intervals);
        int[] tokens = instrumentTokens.Distinct().Select(token => checked((int)token)).ToArray();
        if (tokens.Length == 0) throw new InvalidOperationException("No instruments were selected.");
        string[] selectedIntervals = intervals.Distinct(StringComparer.Ordinal).ToArray();
        if (selectedIntervals.Length == 0) throw new InvalidOperationException("No intervals were selected.");
        ValidateIntervals(selectedIntervals);
        string accessToken = _accessTokenProvider.AccessToken
            ?? throw new InvalidOperationException("An access token is required for historical polling.");
        var model = new HistoricalDataModel(_httpClient, accessToken, _repository)
        {
            Progress = message => progress?.Report(message)
        };
        Task[] workers = tokens.Select(token => RunInstrumentAsync(model, token, selectedIntervals, progress,
            cancellationToken)).ToArray();
        progress?.Report("Polling started for " + workers.Length + " instruments.");
        await Task.WhenAll(workers).ConfigureAwait(false);
        progress?.Report("Polling session completed.");
    }

    private async Task RunInstrumentAsync(HistoricalDataModel model, int instrumentToken, string[] intervals,
        IProgress<string>? progress, CancellationToken cancellationToken)
    {
        DateTime now = MarketNow();
        DateOnly sessionDate = DateOnly.FromDateTime(now);
        ITradingSessionCalendar calendar = await _calendarProvider.LoadAsync(_calendarSelection, instrumentToken,
            MarketTimestamp.FromDateTime(DateTime.UtcNow), cancellationToken).ConfigureAwait(false);
        CalendarLookupResult lookup = calendar.GetSession(instrumentToken, sessionDate);
        if (lookup.Status == CalendarLookupStatus.ClosedDay)
        {
            progress?.Report(instrumentToken + ": polling skipped on closed trading date.");
            return;
        }
        if (lookup.Status != CalendarLookupStatus.Found || lookup.Session == null)
            throw new InvalidOperationException("The configured trading calendar does not cover " + sessionDate + ".");

        DateTime marketOpen = MarketTimestamp.ToDatabase(lookup.Session.OpenedAt);
        DateTime calendarClose = MarketTimestamp.ToDatabase(lookup.Session.ClosedAt);
        (TimeSpan ClosesAt, TimeSpan DailyCandleReadyAt)? sessionOverride =
            await _repository.GetCasSessionOverrideAsync(instrumentToken, cancellationToken).ConfigureAwait(false);
        DateTime marketClose = sessionOverride.HasValue
            ? sessionDate.ToDateTime(TimeOnly.FromTimeSpan(sessionOverride.Value.ClosesAt))
            : calendarClose;
        DateTime dailyReadyAt = sessionDate.ToDateTime(TimeOnly.FromTimeSpan(sessionOverride?.DailyCandleReadyAt
            ?? NonCasDailyReadyAt));
        if (marketClose <= marketOpen || dailyReadyAt <= marketClose)
            throw new InvalidOperationException(instrumentToken + ": invalid close or Daily-ready time.");

        List<IntervalState> states = BuildStates(intervals);
        await BackfillMissingHistoriesAsync(model, instrumentToken, states, marketOpen, sessionDate, progress,
            cancellationToken).ConfigureAwait(false);
        await PollIntradayAsync(model, instrumentToken, states, marketOpen, marketClose, progress, cancellationToken)
            .ConfigureAwait(false);

        TimeSpan dailyWait = dailyReadyAt - MarketNow();
        if (dailyWait > TimeSpan.Zero)
        {
            progress?.Report(instrumentToken + ": waiting for Daily candle at " + dailyReadyAt.ToString("HH:mm:ss") + ".");
            await Task.Delay(dailyWait, cancellationToken).ConfigureAwait(false);
        }
        foreach (IntervalState state in states)
            await TryFetchThroughAsync(model, instrumentToken, state.Interval, state.DatabaseTimeframe, marketClose,
                progress, cancellationToken).ConfigureAwait(false);
        await TryFetchThroughAsync(model, instrumentToken, HistoryCandleInterval.Daily,
            HistoricalStreamTypes.GetDatabaseTimeframe(HistoricalStreamTypes.DailyOneDay),
            sessionDate.AddDays(1).ToDateTime(TimeOnly.MinValue), progress, cancellationToken).ConfigureAwait(false);
        progress?.Report(instrumentToken + ": polling lifecycle completed.");
    }

    private async Task BackfillMissingHistoriesAsync(HistoricalDataModel model, int instrumentToken,
        List<IntervalState> states, DateTime marketOpen, DateOnly sessionDate, IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        foreach (IntervalState state in states)
        {
            DateTime? latest = await _repository.GetLatestCandleTimestampAsync(instrumentToken,
                state.DatabaseTimeframe, cancellationToken).ConfigureAwait(false);
            if (!latest.HasValue)
                await TryFetchRangeAsync(model, instrumentToken, state.Interval, MarketNow().AddYears(-1), marketOpen,
                    progress, cancellationToken).ConfigureAwait(false);
        }
        string dailyTimeframe = HistoricalStreamTypes.GetDatabaseTimeframe(HistoricalStreamTypes.DailyOneDay);
        DateTime? latestDaily = await _repository.GetLatestCandleTimestampAsync(instrumentToken, dailyTimeframe,
            cancellationToken).ConfigureAwait(false);
        if (!latestDaily.HasValue)
            await TryFetchRangeAsync(model, instrumentToken, HistoryCandleInterval.Daily,
                MarketNow().AddYears(-1).Date, sessionDate.ToDateTime(TimeOnly.MinValue), progress, cancellationToken)
                .ConfigureAwait(false);
    }

    private async Task PollIntradayAsync(HistoricalDataModel model, int instrumentToken, List<IntervalState> states,
        DateTime marketOpen, DateTime marketClose, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        while (states.Any(state => state.LastProcessedClose != marketClose))
        {
            cancellationToken.ThrowIfCancellationRequested();
            DateTime now = MarketNow();
            DateTime nextWake = DateTime.MaxValue;
            foreach (IntervalState state in states)
            {
                DateTime? eligibleClose = GetLatestEligibleClose(marketOpen, marketClose, state.Duration,
                    state.GracePeriod, now);
                if (eligibleClose.HasValue
                    && (!state.LastProcessedClose.HasValue || eligibleClose.Value > state.LastProcessedClose.Value))
                {
                    await TryFetchThroughAsync(model, instrumentToken, state.Interval, state.DatabaseTimeframe,
                        eligibleClose.Value, progress, cancellationToken).ConfigureAwait(false);
                    state.LastProcessedClose = eligibleClose.Value;
                }
                DateTime due = GetNextDue(state, marketOpen, marketClose);
                if (due < nextWake) nextWake = due;
            }
            if (nextWake == DateTime.MaxValue) return;
            TimeSpan wait = nextWake - MarketNow();
            if (wait > TimeSpan.Zero) await Task.Delay(wait, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task TryFetchThroughAsync(HistoricalDataModel model, int instrumentToken, string interval,
        string databaseTimeframe, DateTime candleEnd, IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        DateTime? latest = await _repository.GetLatestCandleTimestampAsync(instrumentToken, databaseTimeframe,
            cancellationToken).ConfigureAwait(false);
        DateTime candleStart = latest ?? MarketNow().AddYears(-1);
        await TryFetchRangeAsync(model, instrumentToken, interval, candleStart, candleEnd, progress,
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task TryFetchRangeAsync(HistoricalDataModel model, int instrumentToken, string interval,
        DateTime candleStart, DateTime candleEnd, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        if (candleStart >= candleEnd) return;
        try
        {
            progress?.Report(instrumentToken + "/" + interval + " fetch " + candleStart.ToString("s") + ".."
                + candleEnd.ToString("s") + ".");
            await model.ImportCompletedCandleAsync(new[] { instrumentToken }, interval, candleStart, candleEnd,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            Trace.TraceError("{0}/{1} polling failed: {2}", instrumentToken, interval, exception);
            progress?.Report(instrumentToken + "/" + interval + " failed: " + exception.Message);
        }
    }

    private static DateTime? GetLatestEligibleClose(DateTime marketOpen, DateTime marketClose, TimeSpan duration,
        TimeSpan gracePeriod, DateTime now)
    {
        DateTime cutoff = now - gracePeriod;
        if (cutoff >= marketClose) return marketClose;
        if (cutoff < marketOpen + duration) return null;
        long completed = (cutoff - marketOpen).Ticks / duration.Ticks;
        DateTime result = marketOpen.AddTicks(completed * duration.Ticks);
        return result < marketClose ? result : marketClose;
    }

    private static DateTime GetNextDue(IntervalState state, DateTime marketOpen, DateTime marketClose)
    {
        if (state.LastProcessedClose == marketClose) return DateTime.MaxValue;
        DateTime nextClose = state.LastProcessedClose.HasValue
            ? state.LastProcessedClose.Value + state.Duration
            : marketOpen + state.Duration;
        if (nextClose > marketClose) nextClose = marketClose;
        return nextClose + state.GracePeriod;
    }

    private static List<IntervalState> BuildStates(IEnumerable<string> intervals)
    {
        var states = new List<IntervalState>();
        foreach (string interval in intervals)
        {
            if (interval == HistoryCandleInterval.Daily) continue;
            TimeSpan duration = GetDuration(interval);
            states.Add(new IntervalState(interval, HistoricalStreamTypes.GetDatabaseTimeframe(ToStreamType(interval)),
                duration, TimeSpan.FromTicks(Math.Min(duration.Ticks / 10, MaximumGracePeriod.Ticks))));
        }
        return states;
    }

    private static void ValidateIntervals(IEnumerable<string> intervals)
    {
        foreach (string interval in intervals)
            if (interval != HistoryCandleInterval.Daily) ToStreamType(interval);
    }

    private static string ToStreamType(string interval) => interval switch
    {
        HistoryCandleInterval.OneMinute => HistoricalStreamTypes.IntradayOneMinute,
        HistoryCandleInterval.FiveMinutes => HistoricalStreamTypes.IntradayFiveMinute,
        HistoryCandleInterval.FifteenMinutes => HistoricalStreamTypes.IntradayFifteenMinute,
        HistoryCandleInterval.Hour => HistoricalStreamTypes.IntradayOneHour,
        _ => throw new ArgumentException("Unsupported polling interval: " + interval + ".", nameof(interval))
    };

    private static TimeSpan GetDuration(string interval) => interval switch
    {
        HistoryCandleInterval.OneMinute => TimeSpan.FromMinutes(1),
        HistoryCandleInterval.FiveMinutes => TimeSpan.FromMinutes(5),
        HistoryCandleInterval.FifteenMinutes => TimeSpan.FromMinutes(15),
        HistoryCandleInterval.Hour => TimeSpan.FromHours(1),
        _ => throw new ArgumentException("Unsupported intraday polling interval: " + interval + ".", nameof(interval))
    };

    private static DateTime MarketNow() => MarketTimestamp.ToDatabase(MarketTimestamp.FromDateTime(DateTime.UtcNow));

    private sealed class IntervalState
    {
        public IntervalState(string interval, string databaseTimeframe, TimeSpan duration, TimeSpan gracePeriod)
        {
            Interval = interval;
            DatabaseTimeframe = databaseTimeframe;
            Duration = duration;
            GracePeriod = gracePeriod;
        }

        public string Interval { get; }
        public string DatabaseTimeframe { get; }
        public TimeSpan Duration { get; }
        public TimeSpan GracePeriod { get; }
        public DateTime? LastProcessedClose { get; set; }
    }
}
