using System;
using System.Collections.Generic;
using System.Linq;
using AlgoTrading.Backtest.Runtime;
using AlgoTrading.Models;

namespace AlgoTrading.Research.Analysis;

public enum FindingSeverity
{
    Confirmed, Suspected, Unverified, Blocker,
    HighReview,
    Warning,
    Info
}

public sealed record ProvenanceAnomalyFinding
{
    public required string Code { get; init; } // RSL001, RSL002, RSL003
    public required FindingSeverity Severity { get; init; }
    public required int TradeId { get; init; }
    public required string Description { get; init; }
    public DateTime TimestampUtc { get; init; }
}

public sealed class TradeProvenanceEvaluator
{
    /// <summary>
    /// Audits trade execution provenance against signal observation traces (M2-04 & M2-10).
    /// Detects RSL001 (Signal without order/fill), RSL002 (Invalid fill price/time support), 
    /// and RSL003 (Premature or unverified exit sequencing).
    /// </summary>
    public static IReadOnlyList<ProvenanceAnomalyFinding> AuditTradeProvenance(
        IEnumerable<Trade> trades,
        IReadOnlyList<SignalObservation> observations,
        RuleDefinition rule)
    {
        var findings = new List<ProvenanceAnomalyFinding>();
        var tradeList = trades.ToList();

        // If observation data or trades are missing, record unverified state rather than false passes
        if (observations is null || observations.Count == 0)
        {
            foreach (var trade in tradeList)
            {
                findings.Add(new ProvenanceAnomalyFinding
                {
                    Code = "RSL001",
                    Severity = FindingSeverity.Unverified,
                    TradeId = trade.TradeId,
                    Description = "Execution trace observations unavailable; order lifecycle provenance is unverified.",
                    TimestampUtc = trade.EntryTime
                });
            }
            return findings;
        }

        foreach (var trade in tradeList)
        {
            // Match observation near entry time
            var entryObs = observations.FirstOrDefault(o => o.TimestampUtc == trade.EntryTime.ToUniversalTime());

            if (entryObs is null)
            {
                findings.Add(new ProvenanceAnomalyFinding
                {
                    Code = "RSL001",
                    Severity = FindingSeverity.Suspected,
                    TradeId = trade.TradeId,
                    Description = $"Trade #{trade.TradeId} entered at {trade.EntryTime:O} but corresponding signal observation trace was missing.",
                    TimestampUtc = trade.EntryTime
                });
            }
            else if (!entryObs.Entry && rule.Entry?.Enabled == true)
            {
                findings.Add(new ProvenanceAnomalyFinding
                {
                    Code = "RSL001",
                    Severity = FindingSeverity.Confirmed,
                    TradeId = trade.TradeId,
                    Description = $"Trade #{trade.TradeId} executed entry at {trade.EntryTime:O} despite rule evaluation returning false.",
                    TimestampUtc = trade.EntryTime
                });
            }

            // RSL002: Check fill price anomalies against extreme deviations (e.g., negative/zero price or impossible slippage)
            if (trade.EntryPrice <= 0m || trade.ExitPrice <= 0m)
            {
                findings.Add(new ProvenanceAnomalyFinding
                {
                    Code = "RSL002",
                    Severity = FindingSeverity.Confirmed,
                    TradeId = trade.TradeId,
                    Description = $"Trade #{trade.TradeId} recorded non-positive fill price (Entry: {trade.EntryPrice}, Exit: {trade.ExitPrice}).",
                    TimestampUtc = trade.EntryTime
                });
            }

            // RSL003: Check exit reason consistency and sequence validation
            if (trade.ExitReason == ExitReason.ExitCondition)
            {
                var exitObs = observations.FirstOrDefault(o => o.TimestampUtc == trade.ExitTime.ToUniversalTime());
                if (exitObs is not null && !exitObs.Exit && !exitObs.FullExit)
                {
                    findings.Add(new ProvenanceAnomalyFinding
                    {
                        Code = "RSL003",
                        Severity = FindingSeverity.Suspected,
                        TradeId = trade.TradeId,
                        Description = $"Trade #{trade.TradeId} exited via Condition at {trade.ExitTime:O} but trace observation did not record active exit condition.",
                        TimestampUtc = trade.ExitTime
                    });
                }
            }
        }

        return findings;
    }
}