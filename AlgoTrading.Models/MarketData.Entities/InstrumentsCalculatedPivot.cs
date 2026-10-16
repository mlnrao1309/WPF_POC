namespace AlgoTrading.Models.MarketData.Entities;

/// <summary>
/// One row per (InstrumentToken, TimeStamp, TimeFrame) in
/// dbo.Instruments_CalculatedPivots. Produced by the daily and weekly
/// pivot aggregation procedures.
///
/// The screener reads only the standard levels (PP, R1-R5, S1-S5).
/// The Ref* columns and the PPR1/PPS1 trap flags are read by the
/// strategy layer via RefLevels lookups.
/// </summary>
public class InstrumentsCalculatedPivot
{
    // ---- identity ----
    public long InstrumentToken { get; set; }
    public DateTime TimeStamp { get; set; }   // datetime2(3)
    public string TimeFrame { get; set; } = string.Empty;  // 'day' | 'week'

    // ---- OHLC of the current period ----
    public decimal Open { get; set; }
    public decimal High { get; set; }
    public decimal Low { get; set; }
    public decimal Close { get; set; }

    // ---- standard floor pivots ----
    public decimal? PP { get; set; }
    public decimal? R1 { get; set; }
    public decimal? R2 { get; set; }
    public decimal? R3 { get; set; }
    public decimal? R4 { get; set; }
    public decimal? R5 { get; set; }
    public decimal? S1 { get; set; }
    public decimal? S2 { get; set; }
    public decimal? S3 { get; set; }
    public decimal? S4 { get; set; }
    public decimal? S5 { get; set; }

    // ---- pivot trap flags ----
    public bool PPR1 { get; set; }
    public bool PPS1 { get; set; }

    // ---- reference levels (used by strategy layer) ----
    public decimal? Ref_Resistance { get; set; }
    public decimal? Ref_Support { get; set; }

    // ---- reference candles from prior period ----
    public decimal? Ref_StartCandle_Open { get; set; }
    public decimal? Ref_StartCandle_High { get; set; }
    public decimal? Ref_StartCandle_Low { get; set; }
    public decimal? Ref_StartCandle_Close { get; set; }
    public decimal? Ref_EndCandle_Open { get; set; }
    public decimal? Ref_EndCandle_High { get; set; }
    public decimal? Ref_EndCandle_Low { get; set; }
    public decimal? Ref_EndCandle_Close { get; set; }

    // ---- audit ----
    public DateTime CreatedUtc { get; set; }
}