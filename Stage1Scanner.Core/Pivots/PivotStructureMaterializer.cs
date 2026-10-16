using Stage1Scanner.Core.Market;
using Stage1Scanner.Core.Pivots;

namespace Stage1Scanner.Core.ScannerModel;

/// <summary>
/// Materialises the active confirmed D/W/M Traditional pivot set into
/// Instruments.Structures as CriticalLevels, keyed by "{TF}.{Level}"
/// (e.g. "W.R1"). Called once per instrument per Scanner run, before
/// RuleSets are evaluated.
///
/// Only the timeframes supplied to the constructor are materialised
/// (SCN-CFG-006 "configured subset"). Anchors survive across runs on the
/// same source period and are cleared only when the pivot rotates to a
/// new period.
/// </summary>
public sealed class PivotStructureMaterializer : ICriticalLevelHelper
{
    // Two bars per timeframe: the currently forming period (close time in
    // the future) and the previous completed period (close time in the past).
    // PivotResolver picks the latest completed bar before the reference time,
    // so both must be present.
    private const int PivotBarsRequired = 2;

    public PivotStructureMaterializer(IEnumerable<Timeframe> pivotTimeFrames)
    {
        ArgumentNullException.ThrowIfNull(pivotTimeFrames);

        PivotTimeFrames = pivotTimeFrames
            .Where(PivotResolver.IsTraditionalPivotTimeframe)
            .Distinct()
            .ToArray();
    }

    public string Name => "TraditionalPivots";

    public IReadOnlyList<Timeframe> PivotTimeFrames { get; }

    /// <summary>
    /// Declares the D/W/M timeframes the materialiser needs in order to
    /// resolve the previous completed period. Registered for every configured
    /// pivot timeframe so a requirements-driven loader keeps those series
    /// available even when no rule references them directly.
    /// </summary>
    public void AddRequirements(ScannerRequirements requirements)
    {
        ArgumentNullException.ThrowIfNull(requirements);

        foreach (var tf in PivotTimeFrames)
        {
            requirements.Timeframes.Add(tf);

            if (!requirements.MinimumBarsRequired.TryGetValue(
                    tf, out var current) ||
                current < PivotBarsRequired)
            {
                requirements.MinimumBarsRequired[tf] = PivotBarsRequired;
            }
        }
    }

    public void Prepare(Instruments instruments, DateTimeOffset evaluationTime)
    {
        foreach (var tf in PivotTimeFrames)
        {
            if (!PivotResolver.TryResolveSet(
                    instruments,
                    tf,
                    pivotOffset: 0,
                    referenceTime: evaluationTime,
                    out var pivots))
            {
                continue;
            }

            var code = TimeFrameCode(tf);

            AddLevel(instruments, tf, code, PivotLevel.P,  pivots.P,  pivots);
            AddLevel(instruments, tf, code, PivotLevel.R1, pivots.R1, pivots);
            AddLevel(instruments, tf, code, PivotLevel.R2, pivots.R2, pivots);
            AddLevel(instruments, tf, code, PivotLevel.R3, pivots.R3, pivots);
            AddLevel(instruments, tf, code, PivotLevel.R4, pivots.R4, pivots);
            AddLevel(instruments, tf, code, PivotLevel.R5, pivots.R5, pivots);
            AddLevel(instruments, tf, code, PivotLevel.S1, pivots.S1, pivots);
            AddLevel(instruments, tf, code, PivotLevel.S2, pivots.S2, pivots);
            AddLevel(instruments, tf, code, PivotLevel.S3, pivots.S3, pivots);
            AddLevel(instruments, tf, code, PivotLevel.S4, pivots.S4, pivots);
            AddLevel(instruments, tf, code, PivotLevel.S5, pivots.S5, pivots);
        }
    }

    private static void AddLevel(
        Instruments instruments,
        Timeframe tf,
        string tfCode,
        PivotLevel level,
        decimal price,
        TraditionalPivots pivots)
    {
        var id = $"{tfCode}.{level}";

        // The stable ID ("W.R1") represents the currently effective period.
        // Clear the anchor only when the source period has rotated; keep it
        // otherwise so retest rules see the same anchor across runs.
        if (instruments.Structures.TryGetLevel(id, out var existing) &&
            existing.SourceCloseTime != pivots.SourcePeriodCloseTime)
        {
            instruments.Structures.ClearAnchor(id);
        }

        instruments.Structures.Add(new CriticalLevel(
            Id: id,
            Role: CriticalRole.Pivot,
            Method: pivots.Method,
            Timeframe: tf,
            SourceCloseTime: pivots.SourcePeriodCloseTime,
            Price: price));
    }

    private static string TimeFrameCode(Timeframe tf)
    {
        if (tf == Timeframe.Day) return "D";
        if (tf == Timeframe.Week) return "W";
        if (tf == Timeframe.Month) return "M";
        return tf.ToString();
    }
}