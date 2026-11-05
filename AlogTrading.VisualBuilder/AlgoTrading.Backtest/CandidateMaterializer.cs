using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using AlgoTrading.Models;

namespace AlgoTrading.Backtest;

public sealed record CandidateMetadata
{
    public required string CandidateId { get; init; }
    public required string OriginalStrategyHash { get; init; }
    public required string CandidateHash { get; init; }
    public required RuleDefinition ClonedRule { get; init; }
    public required string CandidateJson { get; init; }
    public required Dictionary<string, decimal> BoundParameters { get; init; }
    public bool IsValid { get; init; }
    public IReadOnlyList<string> ValidationErrors { get; init; } = Array.Empty<string>();
}

public static class CandidateMaterializer
{
    /// <summary>
    /// Materializes an immutable candidate rule from original JSON bytes and a set of bound values,
    /// validates it, and generates cryptographic lineage hashes.
    /// </summary>
    public static CandidateMetadata Materialize(
        string originalStrategyJson,
        string originalHash,
        string candidateId,
        IReadOnlyList<SweepBindingSpec> bindings,
        decimal[] tupleValues,
        Func<RuleDefinition, IEnumerable<string>>? validityCheck = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(originalStrategyJson);
        ArgumentException.ThrowIfNullOrWhiteSpace(originalHash);
        ArgumentNullException.ThrowIfNull(bindings);
        ArgumentNullException.ThrowIfNull(tupleValues);

        // 1. Clone original JSON safely without mutating source
        var cloneObj = JsonNode.Parse(originalStrategyJson)!.AsObject();

        // 2. Apply sidecar tuple values atomically
        SidecarBindingApplicator.ApplyTuple(cloneObj, bindings, tupleValues);
        string candidateJson = cloneObj.ToJsonString();

        // 3. Deserialize and validate schema/semantics
        RuleDefinition clonedRule;
        try
        {
            clonedRule = RuleDefinitionJson.Deserialize(candidateJson);
        }
        catch (Exception ex)
        {
            return new CandidateMetadata
            {
                CandidateId = candidateId,
                OriginalStrategyHash = originalHash,
                CandidateHash = string.Empty,
                ClonedRule = null!,
                CandidateJson = candidateJson,
                BoundParameters = BuildParamDict(bindings, tupleValues),
                IsValid = false,
                ValidationErrors = new[] { $"Deserialization failed: {ex.Message}" }
            };
        }

        var validatorErrors = RuleDefinitionValidator.Validate(clonedRule).Errors.Select(e => e.ToString()).ToList();

        if (validatorErrors.Count == 0 && validityCheck != null)
        {
            validatorErrors.AddRange(validityCheck(clonedRule));
        }

        // 4. Compute candidate SHA-256 hash
        string candidateHash;
        using (var sha = SHA256.Create())
        {
            candidateHash = Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(candidateJson))).ToLowerInvariant();
        }

        return new CandidateMetadata
        {
            CandidateId = candidateId,
            OriginalStrategyHash = originalHash,
            CandidateHash = candidateHash,
            ClonedRule = clonedRule,
            CandidateJson = candidateJson,
            BoundParameters = BuildParamDict(bindings, tupleValues),
            IsValid = validatorErrors.Count == 0,
            ValidationErrors = validatorErrors
        };
    }

    private static Dictionary<string, decimal> BuildParamDict(IReadOnlyList<SweepBindingSpec> bindings, decimal[] values)
    {
        var d = new Dictionary<string, decimal>(bindings.Count, StringComparer.Ordinal);
        for (int i = 0; i < bindings.Count; i++)
        {
            d[bindings[i].Name] = values[i];
        }
        return d;
    }
}

public static class GridPreflightValidator
{
    /// <summary>
    /// Validates grid dimensions, checks overflow safety, enforces maxCombinations preflight cap,
    /// and ensures the shared M2 snapshot provides sufficient historical warmup for indicator parameters.
    /// </summary>
    public static void PreflightGrid(
        IReadOnlyList<SweepBindingSpec> bindings,
        int maxCombinations,
        int availableSnapshotBarCount,
        int requiredMaxWarmupBars)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        if (maxCombinations < 1)
            throw new ArgumentOutOfRangeException(nameof(maxCombinations), "maxCombinations must be >= 1.");

        long totalCombos = 1L;
        foreach (var binding in bindings)
        {
            if (binding.Values is null || binding.Values.Count == 0)
                throw new ArgumentException($"Binding '{binding.Name}' has empty domain values.");

            try
            {
                checked
                {
                    totalCombos *= binding.Values.Count;
                }
            }
            catch (OverflowException)
            {
                throw new InvalidOperationException($"Cartesian grid enumeration overflowed for binding '{binding.Name}'.");
            }
        }

        if (totalCombos > maxCombinations)
        {
            throw new ArgumentException($"Preflight failed: Sweep grid requires {totalCombos} combinations, exceeding the strict preflight cap of {maxCombinations}.");
        }

        // M3-06 Warmup preflight against shared M2 snapshot
        if (availableSnapshotBarCount > 0 && requiredMaxWarmupBars > availableSnapshotBarCount)
        {
            throw new InvalidOperationException($"Preflight failed: Required indicator warmup ({requiredMaxWarmupBars} bars) exceeds available M2 snapshot bar count ({availableSnapshotBarCount}).");
        }
    }
}