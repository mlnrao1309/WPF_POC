using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace AlgoTrading.Research.Analysis;

public sealed class CandidatePatch
{
    public required string Description { get; init; }

    /// <summary>
    /// SHA-256 hex of the parent JSON this patch was authored against.
    /// If set, CandidateGenerator rejects the patch when the actual parent
    /// hash differs, preventing stale-patch application.
    /// </summary>
    public string? ExpectedParentHash { get; init; }

    public required List<JsonPatchOperation> Operations { get; init; }
}

public sealed class JsonPatchOperation
{
    public required string Op { get; init; }      // "replace" | "add" | "remove" | "test"
    public required string Path { get; init; }    // RFC 6901 JSON pointer
    public JsonNode? Value { get; init; }
}