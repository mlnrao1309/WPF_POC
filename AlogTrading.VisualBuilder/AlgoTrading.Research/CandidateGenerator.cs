using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AlgoTrading.Models;

namespace AlgoTrading.Research.Analysis;

public sealed class CandidateResult
{
    public required string OriginalHash { get; init; }
    public required string CandidateHash { get; init; }
    public required string CandidateJson { get; init; }
    public required bool IsValid { get; init; }
    public required List<ValidationError> ValidationErrors { get; init; }
    public IReadOnlyList<AppliedPatchRecord> AppliedPatches { get; init; } = Array.Empty<AppliedPatchRecord>();
}

public sealed record AppliedPatchRecord
{
    public required string Op { get; init; }
    public required string Path { get; init; }
    public bool HadOldValue { get; init; }
    public string? OldValueJson { get; init; }
    public string? NewValueJson { get; init; }
    /// <summary>For array ops, the concrete index where the operation was applied.</summary>
    public int? ResolvedArrayIndex { get; init; }
    // Stored once on the first record so persisted history can restore exact source bytes.
    public string? OriginalDocumentJson { get; init; }
    public string? OriginalDocumentHash { get; init; }
    public string? CandidateDocumentHash { get; init; }
}

public static class CandidateGenerator
{
    public static CandidateResult Generate(string originalJson, CandidatePatch patch)
    {
        var originalHash = ComputeHash(originalJson);

        if (!string.IsNullOrEmpty(patch.ExpectedParentHash)
            && !string.Equals(patch.ExpectedParentHash, originalHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Parent hash mismatch. Expected {patch.ExpectedParentHash}, got {originalHash}.");
        }

        var (candidateJson, applied) = ApplyPatch(originalJson, patch);
        var candidateHash = ComputeHash(candidateJson);

        bool isValid;
        List<ValidationError> validationErrors;
        try
        {
            var rule = RuleDefinitionJson.Deserialize(candidateJson);
            var validation = RuleDefinitionValidator.Validate(rule);
            isValid = validation.IsValid;
            validationErrors = validation.Errors.ToList();
        }
        catch (Exception ex)
        {
            isValid = false;
            validationErrors = new List<ValidationError>
            {
                new("CANDIDATE_PARSE", $"Candidate JSON failed to parse: {ex.Message}", "candidate")
            };
        }

        return new CandidateResult
        {
            OriginalHash = originalHash,
            CandidateHash = candidateHash,
            CandidateJson = candidateJson,
            IsValid = isValid,
            ValidationErrors = validationErrors,
            AppliedPatches = applied
        };
    }

    /// <summary>
    /// Reconstruct the original JSON by reversing the recorded patch operations.
    /// Caller is expected to verify the resulting hash equals OriginalHash.
    /// </summary>
    public static string Revert(string candidateJson, IEnumerable<AppliedPatchRecord> appliedPatches)
    {
        var records = appliedPatches.ToList();
        if (records.Count == 0) return candidateJson;
        var provenance = records[0];
        if (provenance.CandidateDocumentHash is not null
            && !string.Equals(ComputeHash(candidateJson), provenance.CandidateDocumentHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Candidate hash mismatch; refusing to revert a different document.");
        var root = JsonNode.Parse(candidateJson)?.AsObject()
            ?? throw new InvalidOperationException("Candidate JSON is not a JSON object.");
        var reversed = records.AsEnumerable().Reverse().ToList();

        foreach (var record in reversed)
        {
            var tokens = DecodePointer(record.Path);

            JsonNode? parent = root;
            for (int i = 0; i < tokens.Length - 1; i++)
            {
                parent = parent switch
                {
                    JsonObject obj when obj.TryGetPropertyValue(tokens[i], out var next) => next,
                    JsonArray arr when int.TryParse(tokens[i], out int idx) && idx >= 0 && idx < arr.Count => arr[idx],
                    _ => throw new InvalidOperationException(
                        $"Revert cannot navigate to '{record.Path}' at segment '{tokens[i]}'.")
                };
            }

            var lastToken = tokens[^1];

            switch (record.Op)
            {
                case "replace":
                    {
                        if (parent is JsonObject obj)
                            obj[lastToken] = ParseOldValue(record.OldValueJson);
                        else if (parent is JsonArray arr && record.ResolvedArrayIndex is int idx)
                            arr[idx] = ParseOldValue(record.OldValueJson);
                        else
                            throw new InvalidOperationException($"Revert 'replace' cannot resolve '{record.Path}'.");
                        break;
                    }
                case "add":
                    {
                        if (record.HadOldValue)
                        {
                            // add overwrote an existing value — reverse is restore
                            if (parent is JsonObject obj)
                                obj[lastToken] = ParseOldValue(record.OldValueJson);
                            else
                                throw new InvalidOperationException($"Revert 'add-with-old' cannot resolve '{record.Path}'.");
                        }
                        else
                        {
                            // add created a new value — reverse is remove
                            if (parent is JsonObject obj)
                                obj.Remove(lastToken);
                            else if (parent is JsonArray arr && record.ResolvedArrayIndex is int idx)
                                arr.RemoveAt(idx);
                            else
                                throw new InvalidOperationException($"Revert 'add-new' cannot resolve '{record.Path}'.");
                        }
                        break;
                    }
                case "remove":
                    {
                        // remove deleted a value — reverse is insert back
                        if (parent is JsonObject obj)
                            obj[lastToken] = ParseOldValue(record.OldValueJson);
                        else if (parent is JsonArray arr && record.ResolvedArrayIndex is int idx)
                            arr.Insert(idx, ParseOldValue(record.OldValueJson));
                        else
                            throw new InvalidOperationException($"Revert 'remove' cannot resolve '{record.Path}'.");
                        break;
                    }
                case "test":
                    break;  // no-op
                default:
                    throw new InvalidOperationException($"Revert does not support op '{record.Op}'.");
            }
        }

        if (provenance.OriginalDocumentJson is not null)
        {
            if (!string.Equals(ComputeHash(provenance.OriginalDocumentJson), provenance.OriginalDocumentHash, StringComparison.OrdinalIgnoreCase)
                || !JsonNode.DeepEquals(root, JsonNode.Parse(provenance.OriginalDocumentJson)))
                throw new InvalidOperationException("Reverted document does not match its recorded parent.");
            return provenance.OriginalDocumentJson;
        }
        // Compatibility for histories authored before exact-source provenance was introduced.
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static JsonNode? ParseOldValue(string? json)
        => json is null ? null : JsonNode.Parse(json);

    private static (string Json, List<AppliedPatchRecord> Applied) ApplyPatch(string originalJson, CandidatePatch patch)
    {
        var root = JsonNode.Parse(originalJson)?.AsObject()
            ?? throw new InvalidOperationException("Original JSON is not a JSON object.");

        var applied = new List<AppliedPatchRecord>();
        foreach (var op in patch.Operations)
            applied.Add(ApplyOperation(root, op));

        var json = applied.Count == 0 ? originalJson : root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        if (applied.Count > 0)
            applied[0] = applied[0] with
            {
                OriginalDocumentJson = originalJson,
                OriginalDocumentHash = ComputeHash(originalJson),
                CandidateDocumentHash = ComputeHash(json)
            };
        return (json, applied);
    }

    private static AppliedPatchRecord ApplyOperation(JsonNode root, JsonPatchOperation op)
    {
        var tokens = DecodePointer(op.Path);

        JsonNode? parent = root;
        for (int i = 0; i < tokens.Length - 1; i++)
        {
            parent = parent switch
            {
                JsonObject obj when obj.TryGetPropertyValue(tokens[i], out var next) && next is not null => next,
                JsonArray arr when int.TryParse(tokens[i], out int idx) && idx >= 0 && idx < arr.Count => arr[idx],
                _ => throw new InvalidOperationException($"Patch '{op.Op}' cannot navigate to '{op.Path}'.")
            };
        }

        var lastToken = tokens[^1];
        string? oldValueJson = null;
        bool hadOldValue = false;
        int? resolvedArrayIndex = null;

        switch (op.Op)
        {
            case "replace":
                {
                    if (parent is JsonObject obj)
                    {
                        if (!obj.TryGetPropertyValue(lastToken, out var old))
                            throw new InvalidOperationException($"Patch 'replace' targets missing property at '{op.Path}'.");
                        hadOldValue = true;
                        oldValueJson = old?.ToJsonString();
                        obj[lastToken] = op.Value?.DeepClone();
                    }
                    else if (parent is JsonArray arr && int.TryParse(lastToken, out int idx))
                    {
                        if (idx < 0 || idx >= arr.Count)
                            throw new InvalidOperationException($"Patch 'replace' array index out of range at '{op.Path}'.");
                        hadOldValue = true;
                        oldValueJson = arr[idx]?.ToJsonString();
                        resolvedArrayIndex = idx;
                        arr[idx] = op.Value?.DeepClone();
                    }
                    else
                        throw new InvalidOperationException($"Patch 'replace' cannot be applied at '{op.Path}'.");
                    break;
                }
            case "add":
                {
                    if (parent is JsonObject obj)
                    {
                        if (obj.TryGetPropertyValue(lastToken, out var old))
                        {
                            hadOldValue = true;
                            oldValueJson = old?.ToJsonString();
                        }
                        obj[lastToken] = op.Value?.DeepClone();
                    }
                    else if (parent is JsonArray arr)
                    {
                        if (lastToken == "-")
                        {
                            resolvedArrayIndex = arr.Count;
                            arr.Add(op.Value?.DeepClone());
                        }
                        else if (int.TryParse(lastToken, out int idx) && idx >= 0 && idx <= arr.Count)
                        {
                            resolvedArrayIndex = idx;
                            arr.Insert(idx, op.Value?.DeepClone());
                        }
                        else
                            throw new InvalidOperationException($"Patch 'add' invalid array index '{lastToken}' at '{op.Path}'.");
                    }
                    else
                        throw new InvalidOperationException($"Patch 'add' cannot be applied at '{op.Path}'.");
                    break;
                }
            case "remove":
                {
                    if (parent is JsonObject obj)
                    {
                        if (!obj.TryGetPropertyValue(lastToken, out var old))
                            throw new InvalidOperationException($"Patch 'remove' targets missing property at '{op.Path}'.");
                        hadOldValue = true;
                        oldValueJson = old?.ToJsonString();
                        obj.Remove(lastToken);
                    }
                    else if (parent is JsonArray arr && int.TryParse(lastToken, out int idx))
                    {
                        if (idx < 0 || idx >= arr.Count)
                            throw new InvalidOperationException($"Patch 'remove' array index out of range at '{op.Path}'.");
                        hadOldValue = true;
                        oldValueJson = arr[idx]?.ToJsonString();
                        resolvedArrayIndex = idx;
                        arr.RemoveAt(idx);
                    }
                    else
                        throw new InvalidOperationException($"Patch 'remove' cannot be applied at '{op.Path}'.");
                    break;
                }
            case "test":
                {
                    JsonNode? actual;
                    if (parent is JsonObject obj && obj.TryGetPropertyValue(lastToken, out actual)) { }
                    else if (parent is JsonArray arr && int.TryParse(lastToken, out int idx) && idx >= 0 && idx < arr.Count)
                        actual = arr[idx];
                    else
                        throw new InvalidOperationException($"Patch 'test' targets a missing value at '{op.Path}'.");
                    if (!JsonNode.DeepEquals(actual, op.Value))
                        throw new InvalidOperationException($"Patch 'test' failed at '{op.Path}'.");
                    break;
                }
            default:
                throw new InvalidOperationException($"Patch op '{op.Op}' is not supported at '{op.Path}'.");
        }

        return new AppliedPatchRecord
        {
            Op = op.Op,
            Path = op.Path,
            HadOldValue = hadOldValue,
            OldValueJson = oldValueJson,
            NewValueJson = op.Value?.ToJsonString(),
            ResolvedArrayIndex = resolvedArrayIndex
        };
    }

    private static string[] DecodePointer(string pointer)
    {
        if (string.IsNullOrEmpty(pointer) || pointer[0] != '/')
            throw new InvalidOperationException($"JSON pointer '{pointer}' must start with '/'.");
        return pointer.Substring(1)
            .Split('/')
            .Select(seg => seg.Replace("~1", "/").Replace("~0", "~"))
            .ToArray();
    }

    private static string ComputeHash(string input)
    {
        using var sha = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(input);
        return Convert.ToHexString(sha.ComputeHash(bytes)).ToLowerInvariant();
    }
}