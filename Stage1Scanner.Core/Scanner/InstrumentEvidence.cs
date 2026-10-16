using Stage1Scanner.Core.Market;

namespace Stage1Scanner.Core.ScannerModel;

/// <summary>
/// Audit record for a single matched classification in a Scanner run.
/// Captures the classification, evaluation time, controlled reason identifiers
/// from SRS §16.1, Rule/RuleSet paths, and resolved operand values that
/// produced the match. SCN-OUT-002 / SCN-OUT-003 / SCN-OUT-004.
/// </summary>
public sealed record InstrumentEvidence(
    Instruments Instrument,
    string Classification,
    DateTimeOffset EvaluationTime,
    IReadOnlyList<string> ReasonIds,
    IReadOnlyList<string> RulePaths,
    IReadOnlyList<EvidenceOperand> Operands);

/// <summary>
/// A resolved operand captured for evidence. TimeFrame is null for operands
/// that do not resolve to a specific timeframe (e.g. numeric constants).
/// Offset is the closed-candle offset used at evaluation time.
/// Value is null when the operand resolved to Unknown.
/// </summary>
public sealed record EvidenceOperand(
    string Name,
    string? TimeFrame,
    int Offset,
    decimal? Value);