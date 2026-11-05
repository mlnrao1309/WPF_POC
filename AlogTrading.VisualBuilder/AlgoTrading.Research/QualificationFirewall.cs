using System;
using System.Collections.Generic;
using System.Linq;
using AlgoTrading.Backtest.Results;
using AlgoTrading.Models;
using AlgoTrading.Research.Persistence;

namespace AlgoTrading.Research.Analysis;

public sealed record QualificationResult
{
    public required bool IsQualified { get; init; }
    public required string Reason { get; init; }
    public IReadOnlyList<string> FailedGates { get; init; } = Array.Empty<string>();
}

public sealed class QualificationPolicy
{
    public bool RequireCommittedEvidence { get; init; } = true;
    public bool RequireValidSnapshot { get; init; } = true;
    public bool RequireCleanProvenance { get; init; } = true;
    public int MinRequiredTrades { get; init; } = 1;
    public decimal MinSharpe { get; init; } = 0.0m;
}

public static class QualificationFirewall
{
    /// <summary>
    /// Enforces the strict M2-15 qualification firewall gates on a backtest run and its artifacts.
    /// Separates a mathematically 'Completed' calculation from a research-qualified outcome.
    /// </summary>
    public static QualificationResult EvaluateCandidate(
        BacktestResult runResult,
        string researchRoot,
        string researchId,
        string runId,
        string expectedSnapshotId,
        IReadOnlyList<ProvenanceAnomalyFinding>? findings,
        QualificationPolicy? policy = null)
    {
        policy ??= new QualificationPolicy();
        var failedGates = new List<string>();

        // Gate 1: Check basic calculation success & correctness
        if (runResult is null || runResult.Metrics is null)
        {
            return new QualificationResult
            {
                IsQualified = false,
                Reason = "Calculation result or metrics are null.",
                FailedGates = new[] { "Gate1_CalculationSuccess" }
            };
        }

        // Gate 2: M2-06 Committed evidence verification & M2-13 snapshot binding
        if (policy.RequireCommittedEvidence)
        {
            var reader = new ResearchReopenReader(researchRoot);
            if (!reader.TryVerifyAndOpenRun(researchId, runId, out var manifest) || manifest is null || !manifest.Complete)
            {
                failedGates.Add("Gate2_CommittedEvidenceVerified");
            }
            else if (!string.IsNullOrEmpty(expectedSnapshotId) && !string.Equals(manifest.SnapshotId, expectedSnapshotId, StringComparison.Ordinal))
            {
                failedGates.Add("Gate3_SnapshotIdentityMatch");
            }
        }

        // Gate 3: M2-10 Provenance anomaly checks (blocking confirmed findings)
        if (policy.RequireCleanProvenance && findings != null)
        {
            var blockingFindings = findings.Where(f => f.Severity == FindingSeverity.Confirmed).ToList();
            if (blockingFindings.Count > 0)
            {
                failedGates.Add("Gate4_CleanProvenance");
            }
        }

        // Gate 4: Trade sample size and objective risk thresholds
        if (runResult.Metrics.TotalTrades < policy.MinRequiredTrades)
        {
            failedGates.Add("Gate5_SamplePolicyMinimumTrades");
        }

        if (runResult.Metrics.Sharpe < policy.MinSharpe)
        {
            failedGates.Add("Gate6_ObjectiveSharpeThreshold");
        }

        if (failedGates.Count > 0)
        {
            return new QualificationResult
            {
                IsQualified = false,
                Reason = $"Candidate failed {failedGates.Count} qualification gate(s).",
                FailedGates = failedGates
            };
        }

        return new QualificationResult
        {
            IsQualified = true,
            Reason = "All hard qualification gates passed successfully.",
            FailedGates = Array.Empty<string>()
        };
    }
}