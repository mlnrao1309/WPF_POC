using AlgoTrading.Data;
using AlgoTrading.Models;

namespace AlgoTrading.Backtest.Runtime;

/// <summary>
/// A series of bars at one timeframe, together with a mapping from each
/// base bar to the index of the most-recent completed bar of THIS timeframe
/// at that base bar (Section 1.9, point 6).
/// </summary>
public sealed class AlignedSeries
{
    public required IBarSeries Bars { get; init; }
    public required Timeframe Timeframe { get; init; }

    /// <summary>
    /// BaseToTfIndex[i] is the tf bar index visible at base bar i, or -1
    /// when no tf bar has completed yet.
    /// </summary>
    public required int[] BaseToTfIndex { get; init; }

    public int TfBarCount => Bars.Count;
    public int BaseBarCount => BaseToTfIndex.Length;

    public ReadOnlySpan<decimal> GetFieldSpan(string field) => field switch
    {
        "Open" => Bars.Open.Span,
        "High" => Bars.High.Span,
        "Low" => Bars.Low.Span,
        "Close" => Bars.Close.Span,
        "Volume" => Bars.Volume.Span,
        "OI" => Bars.OI.Span,
        _ => throw new InvalidOperationException("Unknown field: " + field)
    };
}