using System;
using System.Collections.Generic;
using System.Linq;
using AlgoTrading.Backtest.Results;
using AlgoTrading.Models;

namespace AlgoTrading.Research.Analysis;

public sealed record CandidateGateEvaluationResult
{
    public required bool PassedAllGates { get; init; }
    public IReadOnlyList<string> FailedGates { get; init; } = Array.Empty<string>();
    public required string Reason { get; init; }
}

public static class CandidateCorrectnessGateEvaluator
{
    /// <summary>
    /// Evaluates M2 correctness, evidence presence, and basic data validity gates for a candidate trial (M3-13).
    /// </summary>
    public static CandidateGateEvaluationResult Evaluate(
        BacktestResult? result,
        bool hasCommittedEvidence,
        bool hasBlockingProvenanceAnomalies)
    {
        var failedGates = new List<string>();

        if (result is null || result.Metrics is null)
        {
            return new CandidateGateEvaluationResult
            {
                PassedAllGates = false,
                FailedGates = new[] { "Gate_ResultOrMetricsNull" },
                Reason = "Backtest result or metrics object is null."
            };
        }

        if (!hasCommittedEvidence)
        {
            failedGates.Add("Gate_CommittedEvidenceVerified");
        }

        if (hasBlockingProvenanceAnomalies)
        {
            failedGates.Add("Gate_CleanProvenance");
        }

        if (result.Metrics.TotalTrades == 0)
        {
            failedGates.Add("Gate_SufficientTradesNonZero");
        }

        if (failedGates.Count > 0)
        {
            return new CandidateGateEvaluationResult
            {
                PassedAllGates = false,
                FailedGates = failedGates,
                Reason = $"Candidate failed {failedGates.Count} correctness/evidence gate(s)."
            };
        }

        return new CandidateGateEvaluationResult
        {
            PassedAllGates = true,
            FailedGates = Array.Empty<string>(),
            Reason = "All M2 correctness and evidence gates passed successfully."
        };
    }
}