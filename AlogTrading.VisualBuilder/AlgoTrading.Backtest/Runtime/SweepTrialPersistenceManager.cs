using AlgoTrading.Backtest.Metrics;
using AlgoTrading.Backtest.Results;
using AlgoTrading.Models;
using System;
using System.Collections.Generic;
using System.Text.Json;

namespace AlgoTrading.Backtest.Runtime;

public enum TrialStatus
{
    Planned,
    Completed,
    Invalid,
    Failed,
    SkippedDuplicate,
    Cancelled,
    Qualified
}

public sealed record SweepTrialRecord
{
    public required int TrialIndex { get; init; }
    public required string CandidateId { get; init; }
    public required string ParentHash { get; init; }
    public required string CandidateHash { get; init; }
    public required string SnapshotId { get; init; }
    public required TrialStatus Status { get; init; }
    public required IReadOnlyDictionary<string, decimal> BoundParameters { get; init; }
    public BacktestMetrics? Metrics { get; init; }
    public IReadOnlyList<string> ValidationErrors { get; init; } = Array.Empty<string>();
    public DateTime TimestampUtc { get; init; } = DateTime.UtcNow;
}

public static class SweepTrialPersistenceManager
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Serializes and formats the trial ledger for research auditability and reopen verification.
    /// </summary>
    public static string SerializeTrialLedger(IEnumerable<SweepTrialRecord> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        return JsonSerializer.Serialize(records, JsonOptions);
    }

    /// <summary>
    /// Reopens and deserializes a trial ledger from persistent storage.
    /// </summary>
    public static IReadOnlyList<SweepTrialRecord> DeserializeTrialLedger(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        return JsonSerializer.Deserialize<IReadOnlyList<SweepTrialRecord>>(json, JsonOptions)
               ?? Array.Empty<SweepTrialRecord>();
    }
}