using AlgoTrading.Backtest;
using AlgoTrading.Backtest.Results;
using AlgoTrading.Backtest.Runtime;
using AlgoTrading.Data;
using AlgoTrading.Models;
using AlgoTrading.Models.Analysis;
using AlgoTrading.Research.Analysis;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace AlgoTrading.Research;

public enum ResearchMode { Guided, Auto }

public enum ResearchStatus { Completed, BlockedByPolicy, Failed, Cancelled, Inconclusive }

public enum CandidateDisposition { Accepted, Rejected, AwaitingApproval, Invalid, Failed, Cancelled }

public sealed class ResearchRequest
{
    public required RuleDefinition OriginalRule { get; init; }
    public required IBarSeries BaseSeries { get; init; }
    public required BacktestEngineOptions EngineOptions { get; init; }
    public ResearchMode Mode { get; init; } = ResearchMode.Guided;
    public string? DataSnapshotId { get; init; }
    public int MaxCandidates { get; init; } = 10;
    public IReadOnlyList<CandidatePatch> ApprovedPatches { get; init; } = Array.Empty<CandidatePatch>();
    /// <summary>Guided mode only. Called for each patch; returning false skips execution and records AwaitingApproval.</summary>
    public Func<CandidatePatch, bool>? GuidedApprovalGate { get; init; }
}

public sealed class ResearchOutcome
{
    public required string ResearchId { get; init; }
    public required ResearchStatus Status { get; init; }
    public required string OriginalHash { get; init; }
    public required IReadOnlyList<CandidateRecord> Candidates { get; init; }
    public required IReadOnlyList<StaticFinding> Findings { get; init; }
    public required string StopReason { get; init; }
}

public sealed class CandidateRecord
{
    public required string CandidateId { get; init; }
    public required CandidateDisposition Disposition { get; init; }
    public CandidateResult? Candidate { get; init; }
    public BacktestResult? RunResult { get; init; }
    public string? RejectionReason { get; init; }
}

public static class ResearchCoordinator
{
    public static ResearchOutcome Run(ResearchRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var originalJson = RuleDefinitionJson.Serialize(request.OriginalRule);
        var originalHash = ComputeHash(originalJson);
        var researchId = Guid.NewGuid().ToString("N");

        // M1: static analysis on the baseline
        var findings = RuleStaticAnalyzer.Analyze(request.OriginalRule);
        var blockers = findings.Where(f => f.Severity == FindingSeverity.Blocker).ToList();

        if (blockers.Count > 0)
        {
            return new ResearchOutcome
            {
                ResearchId = researchId,
                Status = ResearchStatus.BlockedByPolicy,
                OriginalHash = originalHash,
                Candidates = Array.Empty<CandidateRecord>(),
                Findings = findings,
                StopReason = $"Baseline has {blockers.Count} blocking finding(s)."
            };
        }

        var candidates = new List<CandidateRecord>();
        var engine = new BacktestEngine(request.EngineOptions);
        int idx = 0;

        foreach (var patch in request.ApprovedPatches)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return new ResearchOutcome
                {
                    ResearchId = researchId,
                    Status = ResearchStatus.Cancelled,
                    OriginalHash = originalHash,
                    Candidates = candidates,
                    Findings = findings,
                    StopReason = "Cancelled by caller."
                };
            }

            if (idx >= request.MaxCandidates)
            {
                return new ResearchOutcome
                {
                    ResearchId = researchId,
                    Status = ResearchStatus.Inconclusive,
                    OriginalHash = originalHash,
                    Candidates = candidates,
                    Findings = findings,
                    StopReason = $"Reached MaxCandidates ({request.MaxCandidates})."
                };
            }

            var candidateId = $"cand_{idx:D4}";

            // Guided: check approval before generating
            if (request.Mode == ResearchMode.Guided && request.GuidedApprovalGate != null)
            {
                bool approved;
                try { approved = request.GuidedApprovalGate(patch); }
                catch (Exception ex)
                {
                    candidates.Add(new CandidateRecord
                    {
                        CandidateId = candidateId,
                        Disposition = CandidateDisposition.Rejected,
                        RejectionReason = $"Approval gate threw: {ex.Message}"
                    });
                    idx++;
                    continue;
                }
                if (!approved)
                {
                    candidates.Add(new CandidateRecord
                    {
                        CandidateId = candidateId,
                        Disposition = CandidateDisposition.AwaitingApproval,
                        RejectionReason = "Guided mode: approval required before execution."
                    });
                    idx++;
                    continue;
                }
            }

            // Generate candidate
            CandidateResult candResult;
            try
            {
                candResult = CandidateGenerator.Generate(originalJson, patch);
            }
            catch (Exception ex)
            {
                candidates.Add(new CandidateRecord
                {
                    CandidateId = candidateId,
                    Disposition = CandidateDisposition.Invalid,
                    RejectionReason = $"Patch failed: {ex.Message}"
                });
                idx++;
                continue;
            }

            if (!candResult.IsValid)
            {
                candidates.Add(new CandidateRecord
                {
                    CandidateId = candidateId,
                    Disposition = CandidateDisposition.Invalid,
                    Candidate = candResult,
                    RejectionReason = "Generated candidate failed validation."
                });
                idx++;
                continue;
            }

            // Run
            try
            {
                var clonedRule = RuleDefinitionJson.Deserialize(candResult.CandidateJson);
                var runResult = engine.Run(clonedRule, request.BaseSeries, cancellationToken);
                candidates.Add(new CandidateRecord
                {
                    CandidateId = candidateId,
                    Disposition = CandidateDisposition.Accepted,
                    Candidate = candResult,
                    RunResult = runResult
                });
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                candidates.Add(new CandidateRecord
                {
                    CandidateId = candidateId,
                    Disposition = CandidateDisposition.Failed,
                    Candidate = candResult,
                    RejectionReason = $"Backtest failed: {ex.Message}"
                });
            }

            idx++;
        }

        return new ResearchOutcome
        {
            ResearchId = researchId,
            Status = ResearchStatus.Completed,
            OriginalHash = originalHash,
            Candidates = candidates,
            Findings = findings,
            StopReason = "All approved patches processed."
        };
    }

    private static string ComputeHash(string s)
    {
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(s))).ToLowerInvariant();
    }
}