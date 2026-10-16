namespace AlgoTrading.Screener.Repositories;

/// <summary>
/// Daily and weekly pivot levels that apply to a single bar.
/// Populated by the screener after fetching Instruments_CalculatedPivots.
/// <see cref="Empty"/> is used when a bar has no pivot data.
///
/// The four trap flags (PPR1, PPS1, WeekPPR1, WeekPPS1) come from the
/// stored procedures usp_AggregateDailyCalculatedPivots and
/// usp_AggregateWeeklyCalculatedPivots. They are true on days where the
/// period's open AND close both sit between the pivot point and the
/// adjacent R1 (or S1) level — the "pivot trap" pattern.
/// </summary>
public sealed record Pivots(
    decimal? PP = null,

    decimal? R1 = null, decimal? R2 = null, decimal? R3 = null,
    decimal? R4 = null, decimal? R5 = null,

    decimal? S1 = null, decimal? S2 = null, decimal? S3 = null,
    decimal? S4 = null, decimal? S5 = null,

    decimal? WeekPP = null,

    decimal? WeekR1 = null, decimal? WeekR2 = null, decimal? WeekR3 = null,
    decimal? WeekR4 = null, decimal? WeekR5 = null,

    decimal? WeekS1 = null, decimal? WeekS2 = null, decimal? WeekS3 = null,
    decimal? WeekS4 = null, decimal? WeekS5 = null,

    // ---- trap flags ----
    bool PPR1 = false,
    bool PPS1 = false,
    bool WeekPPR1 = false,
    bool WeekPPS1 = false)
{
    /// <summary>All-null / all-false instance. Immutable, safe to share.</summary>
    public static readonly Pivots Empty = new();
}