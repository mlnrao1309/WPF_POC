using Dapper;
using Microsoft.Data.SqlClient;

namespace AlgoTrading.RuleEditor.Services;

public sealed class RunListRow
{
    public Guid RunId { get; init; }
    public int StrategyId { get; init; }
    public string StrategyName { get; init; } = "";
    public string Instrument { get; init; } = "";
    public string Direction { get; init; } = "";
    public string RunMode { get; init; } = "";
    public DateTime RunAt { get; init; }
    public string Status { get; init; } = "";
    public string? ErrorMessage { get; init; }
    public decimal? NetPnL { get; init; }
    public long? TotalTrades { get; init; }
}

/// <summary>
/// Editor-side history reads through the CFCore stored procedure.
/// </summary>
public sealed class StrategyRepositoryService
{
    private readonly string _connectionString;

    public StrategyRepositoryService(string? connectionString)
    {
        _connectionString = connectionString ?? "";
    }

    public bool IsAvailable => !string.IsNullOrWhiteSpace(_connectionString);

    public async Task<IReadOnlyList<RunListRow>> LoadRecentRunsAsync(int limit)
    {
        if (!IsAvailable)
            throw new InvalidOperationException("SQL connection is not configured for run history.");
        using var conn = new SqlConnection(_connectionString);
        var rows = await conn.QueryAsync<RunRowDto>("dbo.StrategyBacktest_Recent", new { Limit = limit },
            commandType: System.Data.CommandType.StoredProcedure).ConfigureAwait(false);
        var list = new List<RunListRow>();
        foreach (var r in rows)
        {
            list.Add(new RunListRow
            {
                RunId = r.RunId,
                StrategyId = r.StrategyId,
                StrategyName = r.StrategyName ?? "",
                Instrument = r.Instrument ?? "",
                Direction = r.Direction ?? "",
                RunMode = r.RunMode ?? "",
                RunAt = r.StartedAt.ToOffset(TimeSpan.FromMinutes(330)).DateTime,
                Status = r.Status ?? "",
                ErrorMessage = r.ErrorMessage,
                NetPnL = r.NetPnL,
                TotalTrades = r.TotalTrades
            });
        }
        return list;
    }

    private sealed class RunRowDto
    {
        public Guid RunId { get; set; }
        public int StrategyId { get; set; }
        public string? StrategyName { get; set; }
        public string? Instrument { get; set; }
        public string? Direction { get; set; }
        public string? RunMode { get; set; }
        public DateTimeOffset StartedAt { get; set; }
        public string? Status { get; set; }
        public string? ErrorMessage { get; set; }
        public decimal? NetPnL { get; set; }
        public long? TotalTrades { get; set; }
    }
}
