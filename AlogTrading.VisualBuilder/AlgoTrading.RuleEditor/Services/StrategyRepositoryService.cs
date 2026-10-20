using Dapper;
using Microsoft.Data.SqlClient;

namespace AlgoTrading.RuleEditor.Services;

public sealed class RunListRow
{
    public Guid RunId { get; init; }
    public int StrategyId { get; init; }
    public int? TunedId { get; init; }
    public uint InstrumentToken { get; init; }
    public string RunMode { get; init; } = "";
    public DateTime RunAt { get; init; }
    public string Status { get; init; } = "";
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// Editor-side, read-only access to BacktestRuns (Section 8.11). The daemon
/// owns inserts and updates; the editor only lists recent runs.
/// </summary>
public sealed class StrategyRepositoryService
{
    private readonly string _connectionString;

    public StrategyRepositoryService(string? connectionString)
    {
        _connectionString = connectionString ?? "";
    }

    public bool IsAvailable => !string.IsNullOrWhiteSpace(_connectionString);

    public IReadOnlyList<RunListRow> LoadRecentRuns(int limit)
    {
        if (!IsAvailable) return Array.Empty<RunListRow>();

        const string q = @"
SELECT TOP (@limit) RunId, StrategyId, TunedId, InstrumentToken,
       RunMode, RunAt, Status, ErrorMessage
FROM BacktestRuns
ORDER BY RunAt DESC";

        try
        {
            using var conn = new SqlConnection(_connectionString);
            var rows = conn.Query<RunRowDto>(q, new { limit }).ToList();
            var list = new List<RunListRow>(rows.Count);
            foreach (var r in rows)
            {
                list.Add(new RunListRow
                {
                    RunId = r.RunId,
                    StrategyId = r.StrategyId,
                    TunedId = r.TunedId,
                    InstrumentToken = unchecked((uint)r.InstrumentToken),
                    RunMode = r.RunMode ?? "",
                    RunAt = r.RunAt,
                    Status = r.Status ?? "",
                    ErrorMessage = r.ErrorMessage
                });
            }
            return list;
        }
        catch
        {
            return Array.Empty<RunListRow>();
        }
    }

    private sealed class RunRowDto
    {
        public Guid RunId { get; set; }
        public int StrategyId { get; set; }
        public int? TunedId { get; set; }
        public int InstrumentToken { get; set; }
        public string? RunMode { get; set; }
        public DateTime RunAt { get; set; }
        public string? Status { get; set; }
        public string? ErrorMessage { get; set; }
    }
}