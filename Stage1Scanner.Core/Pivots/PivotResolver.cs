using Stage1Scanner.Core.Market;

namespace Stage1Scanner.Core.Pivots;

public static class PivotResolver
{
    public static bool IsTraditionalPivotTimeframe(Timeframe timeframe)
        => timeframe == Timeframe.Day ||
           timeframe == Timeframe.Week ||
           timeframe == Timeframe.Month;

    /// <summary>
    /// Resolves the Traditional Pivot set effective at referenceTime.
    /// The source candle must have completed strictly before referenceTime.
    /// This keeps an intraday candle at a D/W/M period close inside the pivot
    /// session that was already effective for that candle.
    /// </summary>
    public static bool TryResolve(
        Instruments instruments,
        Timeframe pivotTimeframe,
        PivotLevel level,
        int pivotOffset,
        DateTimeOffset referenceTime,
        out decimal value)
    {
        value = default;

        if (pivotOffset < 0 || !IsTraditionalPivotTimeframe(pivotTimeframe))
        {
            return false;
        }

        if (!instruments.TryGetCandles(pivotTimeframe, out var candles))
        {
            return false;
        }

        var sourceIndex = -1;

        for (var i = candles.Count - 1; i >= 0; i--)
        {
            if (candles[i].CloseTime < referenceTime)
            {
                sourceIndex = i;
                break;
            }
        }

        sourceIndex -= pivotOffset;

        if (sourceIndex < 0 || sourceIndex >= candles.Count)
        {
            return false;
        }

        var source = candles[sourceIndex];
        var pivots = PivotHelpers.Traditional(
            source.High,
            source.Low,
            source.Close,
            TimeFrameCode(pivotTimeframe),
            source.CloseTime);

        value = PivotHelpers.Get(pivots, level);
        return true;
    }

    /// <summary>
    /// Resolves the full Traditional Pivot set (all levels plus provenance)
    /// for a D/W/M timeframe at referenceTime. Used by audit/evidence paths
    /// and by callers that need more than a single level.
    /// </summary>
    public static bool TryResolveSet(
        Instruments instruments,
        Timeframe pivotTimeframe,
        int pivotOffset,
        DateTimeOffset referenceTime,
        out TraditionalPivots pivots)
    {
        pivots = default!;

        if (pivotOffset < 0 || !IsTraditionalPivotTimeframe(pivotTimeframe))
        {
            return false;
        }

        if (!instruments.TryGetCandles(pivotTimeframe, out var candles))
        {
            return false;
        }

        var sourceIndex = -1;

        for (var i = candles.Count - 1; i >= 0; i--)
        {
            if (candles[i].CloseTime < referenceTime)
            {
                sourceIndex = i;
                break;
            }
        }

        sourceIndex -= pivotOffset;

        if (sourceIndex < 0 || sourceIndex >= candles.Count)
        {
            return false;
        }

        var source = candles[sourceIndex];
        pivots = PivotHelpers.Traditional(
            source.High,
            source.Low,
            source.Close,
            TimeFrameCode(pivotTimeframe),
            source.CloseTime);

        return true;
    }

    private static string TimeFrameCode(Timeframe timeframe)
    {
        if (timeframe == Timeframe.Day) return "D";
        if (timeframe == Timeframe.Week) return "W";
        if (timeframe == Timeframe.Month) return "M";
        return timeframe.ToString();
    }
}