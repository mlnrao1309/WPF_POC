using AlgoTrading.Research.Analysis;
using System.Collections.Generic;

namespace AlgoTrading.Models.Analysis;

//public enum FindingSeverity
//{
//    Blocker,
//    HighReview,
//    Warning,
//    Info
//}

public enum FindingCertainty
{
    Confirmed,
    Suspected,
    Indeterminate
}

public sealed class StaticFinding
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Description { get; init; }
    public FindingSeverity Severity { get; init; }
    public FindingCertainty Certainty { get; init; }
    
    /// <summary>
    /// The RFC 6901 JSON pointer to the affected node.
    /// </summary>
    public string? JsonPointer { get; init; }

    public List<CandidatePatch> SuggestedPatches { get; init; } = new();
}