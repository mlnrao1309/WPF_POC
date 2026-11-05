public sealed record BaselineComparabilityResult
{
    public required bool IsComparable { get; init; }
    public required string Reason { get; init; }
    public IReadOnlyList<string> MismatchedFields { get; init; } = Array.Empty<string>();
}

public sealed record ExecutionBaselineContext
{
    public required uint InstrumentToken { get; init; }
    public required string BaseTimeframe { get; init; }
    public required string SnapshotId { get; init; }
    public required DateTime DataFromUtc { get; init; }
    public required DateTime DataToUtc { get; init; }
    public required decimal InitialCapital { get; init; }
    public required string CostConfigJson { get; init; }
    public required string SlippageConfigJson { get; init; }
}

public static class SweepBaselineComparability
{
    /// <summary>
    /// Verifies whether a candidate trial shares identical execution context with the baseline (M3-12).
    /// </summary>
    public static BaselineComparabilityResult VerifyComparability(
        ExecutionBaselineContext baseline,
        ExecutionBaselineContext candidate)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(candidate);

        var mismatches = new List<string>();

        if (baseline.InstrumentToken != candidate.InstrumentToken)
            mismatches.Add(nameof(candidate.InstrumentToken));
        if (!string.Equals(baseline.BaseTimeframe, candidate.BaseTimeframe, StringComparison.Ordinal))
            mismatches.Add(nameof(candidate.BaseTimeframe));
        if (!string.Equals(baseline.SnapshotId, candidate.SnapshotId, StringComparison.Ordinal))
            mismatches.Add(nameof(candidate.SnapshotId));
        if (baseline.DataFromUtc != candidate.DataFromUtc || baseline.DataToUtc != candidate.DataToUtc)
            mismatches.Add("DataDateInterval");
        if (baseline.InitialCapital != candidate.InitialCapital)
            mismatches.Add(nameof(candidate.InitialCapital));
        if (!string.Equals(baseline.CostConfigJson, candidate.CostConfigJson, StringComparison.Ordinal))
            mismatches.Add(nameof(candidate.CostConfigJson));
        if (!string.Equals(baseline.SlippageConfigJson, candidate.SlippageConfigJson, StringComparison.Ordinal))
            mismatches.Add(nameof(candidate.SlippageConfigJson));

        if (mismatches.Count > 0)
        {
            return new BaselineComparabilityResult
            {
                IsComparable = false,
                Reason = $"Candidate trial is incomparable to baseline due to mismatches in: {string.Join(", ", mismatches)}.",
                MismatchedFields = mismatches
            };
        }

        return new BaselineComparabilityResult
        {
            IsComparable = true,
            Reason = "Execution baseline comparability verified successfully.",
            MismatchedFields = Array.Empty<string>()
        };
    }
}