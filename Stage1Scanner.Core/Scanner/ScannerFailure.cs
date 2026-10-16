using Stage1Scanner.Core.Market;

namespace Stage1Scanner.Core.ScannerModel;

/// <summary>
/// Records a non-fatal failure during a Scanner run. A single instrument
/// failing does not abort the run; the failure is captured here for audit.
/// </summary>
public sealed record ScannerFailure(
    Instruments Instrument,
    DateTimeOffset EvaluationTime,
    string ReasonId,
    string Message);