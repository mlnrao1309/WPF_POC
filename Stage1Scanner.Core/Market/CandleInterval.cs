namespace Stage1Scanner.Core.Market;

/// <summary>
/// Derives Candle.CloseTime from OpenTime + Timeframe interval.
/// Calendar timeframes (D/W/M) use exact calendar arithmetic.
/// Intraday timeframes use a nominal interval capped at NSE session close
/// so the truncated last-hour bar does not report a close beyond 15:30.
/// </summary>
internal static class CandleInterval
{
    // NSE regular session close. MCX / F&amp;O segments differ; parameterise
    // per segment when those loaders are wired in.
    private static readonly TimeOnly SessionClose = new(15, 30);

    public static DateTimeOffset CloseTime(DateTimeOffset open, Timeframe timeframe)
    {
        switch (timeframe.Unit)
        {
            case TimeframeUnit.Day:
                return open.AddDays(timeframe.Count);

            case TimeframeUnit.Week:
                // Weekly boundary = next Monday 00:00 relative to open.
                // Using AddDays(7 * Count) keeps it a pure calendar offset
                // and matches the "< CloseTime" comparisons elsewhere.
                return open.AddDays(7 * timeframe.Count);

            case TimeframeUnit.Month:
                return open.AddMonths(timeframe.Count);

            case TimeframeUnit.Minute:
            case TimeframeUnit.Hour:
                return IntradayClose(open, timeframe);

            default:
                throw new NotSupportedException(
                    $"Timeframe unit '{timeframe.Unit}' has no interval mapping.");
        }
    }

    private static DateTimeOffset IntradayClose(DateTimeOffset open, Timeframe timeframe)
    {
        var nominal = open + Duration(timeframe);

        // The regular NSE boundary is an IST wall time, regardless of
        // the offset used by the loader to represent the opening instant.
        var marketOpen = open.ToOffset(TimeSpan.FromMinutes(330));
        var sessionClose = new DateTimeOffset(
            marketOpen.Year, marketOpen.Month, marketOpen.Day,
            SessionClose.Hour, SessionClose.Minute, 0,
            marketOpen.Offset).ToOffset(open.Offset);

        // Evening/special sessions cannot use the regular-session cutoff.
        // Keep the nominal interval; calendar-aware loaders apply the actual
        // session boundary. Never move a candle's close before its open.
        return open < sessionClose && nominal > sessionClose ? sessionClose : nominal;
    }

    private static TimeSpan Duration(Timeframe timeframe)
        => timeframe.Unit switch
        {
            TimeframeUnit.Minute => TimeSpan.FromMinutes(timeframe.Count),
            TimeframeUnit.Hour => TimeSpan.FromHours(timeframe.Count),
            _ => throw new NotSupportedException(
                $"Interval for unit '{timeframe.Unit}' is not an intraday span.")
        };
}
