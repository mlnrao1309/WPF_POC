using System.Collections.Generic;

namespace AlgoTrading.Backtest.Runtime;

public sealed record EvaluationTraceRecord
{
    public required System.DateTime EvaluationTimeUtc { get; init; }
    public required string NodePath { get; init; }
    public required bool IsEnabled { get; init; }
    public required string Operator { get; init; }
    public required string Result { get; init; }

    public decimal? LeftValue { get; init; }
    public decimal? RightValue { get; init; }
    public System.DateTimeOffset? LeftAvailableAt { get; init; }
    public System.DateTimeOffset? RightAvailableAt { get; init; }
    public decimal? PreviousLeftValue { get; init; }
    public decimal? PreviousRightValue { get; init; }

    // M2: Reference revision evidence
    public string? LeftReferenceId { get; init; }
    public int? LeftReferenceCapturedBarIndex { get; init; }
    public decimal? LeftReferenceCapturedValue { get; init; }
    public string? RightReferenceId { get; init; }
    public int? RightReferenceCapturedBarIndex { get; init; }
    public decimal? RightReferenceCapturedValue { get; init; }
}