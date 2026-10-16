using Stage1Scanner.Core.Market;

namespace Stage1Scanner.Core.ScannerModel;

/// <summary>
/// Records an instrument whose classification could not be determined
/// because required data was unavailable (RuleTruth.Unknown).
/// One record per instrument; <see cref="Classifications"/> lists which
/// top-level RuleSets evaluated Unknown for that instrument.
/// </summary>
public sealed record ScannerInsufficientData(
    Instruments Instrument,
    DateTimeOffset EvaluationTime,
    IReadOnlyList<string> Classifications);