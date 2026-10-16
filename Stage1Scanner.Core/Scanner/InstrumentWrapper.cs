using Stage1Scanner.Core.Market;

namespace Stage1Scanner.Core.ScannerModel;

/// <summary>
/// Scanner output bucket. Entry/Exit/Order behaviour belongs to the later
/// post-scanner stage and is intentionally not implemented here yet.
/// </summary>
public sealed class InstrumentWrapper
{
    public InstrumentWrapper(
        TrendDirection direction,
        IEnumerable<Instruments>? instruments = null,
        IEnumerable<InstrumentEvidence>? evidence = null)
    {
        Direction = direction;
        Instruments = (instruments ?? []).ToArray();
        Evidence = (evidence ?? []).ToArray();
    }

    public TrendDirection Direction { get; }
    public IReadOnlyList<Instruments> Instruments { get; }

    /// <summary>
    /// Evidence records for the classified instruments in this wrapper
    /// (SCN-OUT-002). One entry per classification match. Rule-level detail
    /// (paths, resolved operands) fills in once <c>RuleSet.Evaluate</c> exposes
    /// rule-level results.
    /// </summary>
    public IReadOnlyList<InstrumentEvidence> Evidence { get; }
}

public sealed class InstrumentWrapperEventArgs : EventArgs
{
    public InstrumentWrapperEventArgs(InstrumentWrapper wrapper)
    {
        Wrapper = wrapper ?? throw new ArgumentNullException(nameof(wrapper));
    }

    public InstrumentWrapper Wrapper { get; }
}

public sealed record ScannerConflict(
    Instruments Instruments,
    DateTimeOffset EvaluationTime,
    string Reason);