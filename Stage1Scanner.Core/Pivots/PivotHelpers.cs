using Stage1Scanner.Core.Market;

namespace Stage1Scanner.Core.Pivots;

/// <summary>
/// Traditional (Floor Trader) pivot set: P plus R1-R5 and S1-S5.
/// Carries enough provenance (timeframe, method, source close time,
/// developing flag) to enter the Critical-Level layer without ambiguity.
/// </summary>
public sealed record TraditionalPivots(
    decimal P,
    decimal R1,
    decimal R2,
    decimal R3,
    decimal R4,
    decimal R5,
    decimal S1,
    decimal S2,
    decimal S3,
    decimal S4,
    decimal S5,
    string TimeFrame = "",
    string Method = "Traditional",
    DateTimeOffset SourcePeriodCloseTime = default,
    bool IsDeveloping = false);

public enum PivotLevel
{
    P,
    R1,
    R2,
    R3,
    R4,
    R5,
    S1,
    S2,
    S3,
    S4,
    S5
}

/// <summary>
/// A Pivot Trap fact: Open and Close both inside the same adjacent pivot band.
/// High/Low may extend outside the band without invalidating the fact.
/// </summary>
public sealed record PivotTrapMatch(
    string Band,
    decimal Low,
    decimal High);

/// <summary>
/// Machine-readable reason identifiers used by Pivot evidence records.
/// Keep in sync with the SRS §16.1 controlled reason vocabulary.
/// </summary>
public static class PivotReasonIds
{
    public const string PivotTrap = "PIVOT_TRAP";
    public const string PInPrevS1S2 = "PIVOT_IN_PREV_S1_S2_BAND";
}

public static class PivotHelpers
{
    /// <summary>
    /// Traditional pivot formulas. Values are rounded to two decimals.
    /// Optional provenance fields feed the Critical-Level audit layer; they do
    /// not change the numeric output.
    /// </summary>
    public static TraditionalPivots Traditional(
        decimal high,
        decimal low,
        decimal close,
        string timeFrame = "",
        DateTimeOffset sourcePeriodCloseTime = default,
        bool isDeveloping = false)
    {
        if (high < low)
        {
            throw new ArgumentException(
                "high must be greater than or equal to low.",
                nameof(high));
        }

        var p = Round2((high + low + close) / 3m);
        var r1 = Round2((p * 2m) - low);
        var s1 = Round2((p * 2m) - high);
        var r2 = Round2(p + (high - low));
        var s2 = Round2(p - (high - low));
        var r3 = Round2((p * 2m) + (high - (2m * low)));
        var s3 = Round2((p * 2m) - ((2m * high) - low));
        var r4 = Round2((p * 3m) + (high - (3m * low)));
        var s4 = Round2((p * 3m) - ((3m * high) - low));
        var r5 = Round2((p * 4m) + (high - (4m * low)));
        var s5 = Round2((p * 4m) - ((4m * high) - low));

        return new TraditionalPivots(
            p,
            r1, r2, r3, r4, r5,
            s1, s2, s3, s4, s5,
            timeFrame,
            "Traditional",
            sourcePeriodCloseTime,
            isDeveloping);
    }

    /// <summary>
    /// Developing Daily pivots - mirrors Pine <c>calcPivots("N", 0)</c>.
    /// Uses the supplied developing Daily bar's High/Low/Close and is marked
    /// <see cref="TraditionalPivots.IsDeveloping"/> = true. Intended for
    /// display/inspection only; SCN-PIV-006 forbids their use as RuleSet operands.
    /// </summary>
    public static TraditionalPivots Developing(
        Candle developingDailyBar,
        string timeFrame = "D")
        => Traditional(
            developingDailyBar.High,
            developingDailyBar.Low,
            developingDailyBar.Close,
            timeFrame,
            developingDailyBar.CloseTime,
            isDeveloping: true);

    public static decimal Get(TraditionalPivots pivots, PivotLevel level)
        => level switch
        {
            PivotLevel.P => pivots.P,
            PivotLevel.R1 => pivots.R1,
            PivotLevel.R2 => pivots.R2,
            PivotLevel.R3 => pivots.R3,
            PivotLevel.R4 => pivots.R4,
            PivotLevel.R5 => pivots.R5,
            PivotLevel.S1 => pivots.S1,
            PivotLevel.S2 => pivots.S2,
            PivotLevel.S3 => pivots.S3,
            PivotLevel.S4 => pivots.S4,
            PivotLevel.S5 => pivots.S5,
            _ => throw new ArgumentOutOfRangeException(nameof(level))
        };

    /// <summary>
    /// Pivot Trap - Open and Close both lie inclusively inside the same
    /// adjacent pivot band (P-R1, R1-R2, R2-R3, S1-P, S2-S1, S3-S2).
    /// Returns null when no band contains both endpoints.
    /// </summary>
    public static PivotTrapMatch? GetTrapBand(
        decimal open,
        decimal close,
        TraditionalPivots pivots)
    {
        var bands = new (string Name, decimal Low, decimal High)[]
        {
            ("P-R1", pivots.P, pivots.R1),
            ("R1-R2", pivots.R1, pivots.R2),
            ("R2-R3", pivots.R2, pivots.R3),
            ("S1-P", pivots.S1, pivots.P),
            ("S2-S1", pivots.S2, pivots.S1),
            ("S3-S2", pivots.S3, pivots.S2)
        };

        foreach (var band in bands)
        {
            var low = Math.Min(band.Low, band.High);
            var high = Math.Max(band.Low, band.High);

            if (Between(open, low, high) && Between(close, low, high))
            {
                return new PivotTrapMatch(band.Name, low, high);
            }
        }

        return null;
    }

    /// <summary>
    /// Structural condition from <c>intraday-v4-theta.txt :: whenPlessthanS1</c>:
    /// the current period's pivot P lies between the previous period's S2
    /// (below) and S1 (above).
    /// </summary>
    /// <remarks>
    /// Pine: <c>if (S1[1] &gt; P and S2[1] &lt; P)</c>. Name is descriptive;
    /// the SRS reason identifier is <see cref="PivotReasonIds.PInPrevS1S2"/>.
    /// </remarks>
    public static bool IsPInPrevS1S2Band(
        decimal currentP,
        decimal prevS1,
        decimal prevS2)
        => prevS1 > currentP && prevS2 < currentP;

    public static bool Between(decimal value, decimal lower, decimal upper)
        => value >= lower && value <= upper;

    private static decimal Round2(decimal value)
        => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}

/// <summary>Unrounded Camarilla levels: close +/- 1.1 * range / (12, 6, 4, 2).</summary>
public sealed record CamarillaPivots(
    decimal R4, decimal R3, decimal R2, decimal R1,
    decimal S1, decimal S2, decimal S3, decimal S4)
{
    public static CamarillaPivots Calculate(decimal high, decimal low, decimal close)
    {
        if (high < low || close < low || close > high)
            throw new ArgumentException("Camarilla requires valid source high, low and close bounds.");
        decimal shift = (high - low) * 1.1m;
        return new(close + shift / 2m, close + shift / 4m,
            close + shift / 6m, close + shift / 12m,
            close - shift / 12m, close - shift / 6m,
            close - shift / 4m, close - shift / 2m);
    }
}
