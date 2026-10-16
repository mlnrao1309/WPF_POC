namespace Stage1Scanner.Core.Market;

using Stage1Scanner.Core.Sessions;

/// <summary>
/// Aggregates coarser timeframes from finer ones.
/// Weekly aggregation runs Monday 00:00 (inclusive) to next Monday 00:00
/// (exclusive) in the source candles' own offset. No session or calendar
/// table is required — the boundaries are pure calendar arithmetic.
/// </summary>
public static class CandleAggregator
{
    public static Candles Aggregate(
        this Candles source,
        Timeframe target,
        Instruments instrument,
        ISessionProvider sessions)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(instrument);
        ArgumentNullException.ThrowIfNull(sessions);
        if (source.Timeframe == target) return source;

        var sourceDuration = FixedDuration(source.Timeframe);
        var targetDuration = FixedDuration(target);
        if (targetDuration <= sourceDuration || targetDuration.Ticks % sourceDuration.Ticks != 0)
            throw new InvalidOperationException(
                $"Cannot aggregate {source.Timeframe} candles to {target}.");

        var output = new List<Candle>();
        DateTimeOffset? bucketOpen = null;
        DateTimeOffset bucketClose = default;
        DateTimeOffset expectedOpen = default;
        decimal open = 0m, high = 0m, low = 0m, close = 0m, volume = 0m;
        decimal? oi = null;

        foreach (var bar in source)
        {
            var date = DateOnly.FromDateTime(bar.OpenTime.Date);
            var session = sessions.GetSession(instrument, date);
            if (!session.IsTradingDay)
                throw new InvalidDataException($"{instrument.Symbol} has a candle on non-trading day {date:yyyy-MM-dd}.");

            var sessionOpen = At(date, session.Open, bar.OpenTime.Offset);
            var sessionClose = At(date, session.Close, bar.OpenTime.Offset);
            if (bar.OpenTime < sessionOpen || bar.OpenTime >= sessionClose)
                throw new InvalidDataException($"{instrument.Symbol} candle {bar.OpenTime:O} is outside its trading session.");

            var elapsedTicks = (bar.OpenTime - sessionOpen).Ticks;
            var nextBucketOpen = sessionOpen.AddTicks(elapsedTicks / targetDuration.Ticks * targetDuration.Ticks);
            var nominalClose = nextBucketOpen + targetDuration;
            var nextBucketClose = nominalClose < sessionClose ? nominalClose : sessionClose;

            if (bucketOpen != nextBucketOpen)
            {
                if (bucketOpen.HasValue)
                    AddCompleted(output, instrument, source.Timeframe, target, bucketOpen.Value,
                        bucketClose, expectedOpen, open, high, low, close, volume, oi);

                if (bar.OpenTime != nextBucketOpen)
                    throw new InvalidDataException($"{instrument.Symbol} {target} period beginning {nextBucketOpen:O} is incomplete.");

                bucketOpen = nextBucketOpen;
                bucketClose = nextBucketClose;
                expectedOpen = bar.OpenTime;
                open = bar.Open;
                high = bar.High;
                low = bar.Low;
                close = bar.Close;
                volume = bar.Volume;
                oi = bar.OpenInterest;
            }
            else
            {
                if (bar.OpenTime != expectedOpen)
                    throw new InvalidDataException($"{instrument.Symbol} {target} period beginning {bucketOpen:O} has a source-candle gap.");
                if (bar.High > high) high = bar.High;
                if (bar.Low < low) low = bar.Low;
                close = bar.Close;
                volume = checked(volume + bar.Volume);
                oi = bar.OpenInterest;
            }

            var nominalSourceClose = bar.OpenTime + sourceDuration;
            expectedOpen = nominalSourceClose < sessionClose ? nominalSourceClose : sessionClose;
        }

        if (bucketOpen.HasValue)
            AddCompleted(output, instrument, source.Timeframe, target, bucketOpen.Value,
                bucketClose, expectedOpen, open, high, low, close, volume, oi);

        return new Candles(target, output);
    }

    private static void AddCompleted(
        List<Candle> output,
        Instruments instrument,
        Timeframe source,
        Timeframe target,
        DateTimeOffset openTime,
        DateTimeOffset closeTime,
        DateTimeOffset coveredUntil,
        decimal open,
        decimal high,
        decimal low,
        decimal close,
        decimal volume,
        decimal? oi)
    {
        if (coveredUntil != closeTime)
            throw new InvalidDataException(
                $"{instrument.Symbol} {target} period beginning {openTime:O} is incomplete for source {source}.");
        output.Add(new Candle(openTime, closeTime, open, high, low, close, volume, oi));
    }

    private static DateTimeOffset At(DateOnly date, TimeOnly time, TimeSpan offset)
        => new(date.ToDateTime(time, DateTimeKind.Unspecified), offset);

    private static TimeSpan FixedDuration(Timeframe timeframe) => timeframe.Unit switch
    {
        TimeframeUnit.Minute => TimeSpan.FromMinutes(timeframe.Count),
        TimeframeUnit.Hour => TimeSpan.FromHours(timeframe.Count),
        _ => throw new InvalidOperationException($"Generic aggregation supports intraday timeframes only; received {timeframe}.")
    };

    /// <summary>
    /// Aggregates a daily candle series into weekly candles, one per
    /// Monday-to-Sunday week. OHLC merge: Open = first daily open,
    /// High = max, Low = min, Close = last daily close, Volume = sum,
    /// OpenInterest = last daily value. Partial weeks (current or the very
    /// first) are emitted; the consumer decides whether CloseTime has
    /// passed by comparing against its evaluation time.
    /// </summary>
    public static Candles ToWeekly(this Candles daily)
    {
        ArgumentNullException.ThrowIfNull(daily);

        if (daily.Timeframe != Timeframe.Day)
        {
            throw new ArgumentException(
                $"Weekly aggregation requires daily source candles. " +
                $"Received: {daily.Timeframe}.",
                nameof(daily));
        }

        var weekly = new List<Candle>(daily.Count / 5 + 1);

        DateTimeOffset? currentWeekStart = null;
        decimal open = 0m, high = 0m, low = 0m, close = 0m, volume = 0m;
        decimal? lastOi = null;

        foreach (var bar in daily)
        {
            var weekStart = WeekStart(bar.OpenTime);

            if (currentWeekStart != weekStart)
            {
                if (currentWeekStart.HasValue)
                {
                    weekly.Add(BuildWeeklyBar(
                        currentWeekStart.Value,
                        open, high, low, close, volume, lastOi));
                }

                currentWeekStart = weekStart;
                open = bar.Open;
                high = bar.High;
                low = bar.Low;
                close = bar.Close;
                volume = bar.Volume;
                lastOi = bar.OpenInterest;
            }
            else
            {
                if (bar.High > high) high = bar.High;
                if (bar.Low < low) low = bar.Low;
                close = bar.Close;
                volume += bar.Volume;
                lastOi = bar.OpenInterest;
            }
        }

        if (currentWeekStart.HasValue)
        {
            weekly.Add(BuildWeeklyBar(
                currentWeekStart.Value,
                open, high, low, close, volume, lastOi));
        }

        return new Candles(Timeframe.Week, weekly);
    }

    /// <summary>
    /// Monday 00:00 of the week containing <paramref name="date"/>, in the
    /// same UTC offset. Sunday belongs to the week that began the preceding
    /// Monday, so the interval is [Mon 00:00, next Mon 00:00).
    /// </summary>
    internal static DateTimeOffset WeekStart(DateTimeOffset date)
    {
        var midnight = new DateTimeOffset(
            date.Year, date.Month, date.Day, 0, 0, 0, date.Offset);

        // Sunday = 0 .. Saturday = 6; shift to "days since Monday".
        var daysSinceMonday = ((int)midnight.DayOfWeek + 6) % 7;
        return midnight.AddDays(-daysSinceMonday);
    }

    private static Candle BuildWeeklyBar(
        DateTimeOffset weekStart,
        decimal open, decimal high, decimal low, decimal close,
        decimal volume, decimal? openInterest)
        // Candle.Create derives CloseTime = weekStart + 7 days = next Monday 00:00.
        => Candle.Create(
            weekStart,
            Timeframe.Week,
            open, high, low, close,
            volume, openInterest);
}
