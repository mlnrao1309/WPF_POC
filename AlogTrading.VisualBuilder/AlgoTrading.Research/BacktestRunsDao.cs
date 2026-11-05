using AlgoTrading.Models;
using Dapper;
using Microsoft.Data.SqlClient;
using SysDateTime = System.DateTime;

namespace AlgoTrading.Data.Daos;

public sealed class BacktestRunsDao
{
    private readonly string _connectionString;

    public BacktestRunsDao(string connectionString)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
    }

    public void StartRun(Guid runId, string strategyName, string strategyVersion,
        uint instrumentToken, string dataProvider, string executionModel,
        DateTimeOffset from, DateTimeOffset to, string configurationJson, decimal initialCapital)
    {
        using var conn = new SqlConnection(_connectionString);
        conn.Execute("dbo.StrategyBacktest_Start", new
        {
            RunId = runId, StrategyName = strategyName, StrategyVersion = strategyVersion,
            InstrumentToken = checked((int)instrumentToken), DataProvider = dataProvider,
            ExecutionModel = executionModel, BacktestFrom = from, BacktestTo = to,
            ConfigurationJson = configurationJson, InitialCapital = initialCapital
        }, commandType: System.Data.CommandType.StoredProcedure);
    }

    public void CompleteRun(Guid runId, string resultJson, string resultFilePath)
    {
        using var conn = new SqlConnection(_connectionString);
        conn.Execute("dbo.StrategyBacktest_Complete", new
        {
            RunId = runId, ResultJson = resultJson, ResultFilePath = resultFilePath
        }, commandType: System.Data.CommandType.StoredProcedure);
    }

    public void FailRun(Guid runId, string errorMessage, bool cancelled = false)
    {
        using var conn = new SqlConnection(_connectionString);
        conn.Execute("dbo.StrategyBacktest_Fail", new
        {
            RunId = runId, ErrorMessage = errorMessage.Length > 2000 ? errorMessage[..2000] : errorMessage,
            Cancelled = cancelled
        }, commandType: System.Data.CommandType.StoredProcedure);
    }

    public async Task InsertRunningAsync(
        Guid runId, int strategyId, int? tunedId, uint instrumentToken,
        Timeframe baseTimeframe, SysDateTime dataFrom, SysDateTime dataTo,
        string runMode, string engineVersion, string dataSource, string? runBy,
        CancellationToken ct = default)
    {
        const string sql = @"
INSERT INTO BacktestRuns
    (RunId, StrategyId, TunedId, InstrumentToken, BaseTimeframe, DataFrom, DataTo,
     RunMode, RunBy, EngineVersion, DataSource, Status)
VALUES
    (@RunId, @StrategyId, @TunedId, @InstrumentToken, @BaseTimeframe, @DataFrom, @DataTo,
     @RunMode, @RunBy, @EngineVersion, @DataSource, 'Running');";

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(sql, new
        {
            RunId = runId, StrategyId = strategyId, TunedId = tunedId,
            InstrumentToken = (int)instrumentToken,
            BaseTimeframe = baseTimeframe.ToString(),
            DataFrom = dataFrom, DataTo = dataTo,
            RunMode = runMode, RunBy = runBy,
            EngineVersion = engineVersion, DataSource = dataSource
        }, cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task UpdateCompletedAsync(Guid runId, string metricsJson, string resultFilePath, CancellationToken ct = default)
    {
        const string sql = @"
UPDATE BacktestRuns
SET MetricsJson = @MetricsJson, ResultFilePath = @ResultFilePath,
    Status = 'Completed', ErrorMessage = NULL
WHERE RunId = @RunId;";

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(sql, new
        {
            RunId = runId, MetricsJson = metricsJson, ResultFilePath = resultFilePath
        }, cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task UpdateFailedAsync(Guid runId, string errorMessage, CancellationToken ct = default)
    {
        const string sql = @"
UPDATE BacktestRuns SET Status = 'Failed', ErrorMessage = @ErrorMessage WHERE RunId = @RunId;";

        string truncated = errorMessage.Length > 2000 ? errorMessage.Substring(0, 2000) : errorMessage;

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(sql, new
        {
            RunId = runId, ErrorMessage = truncated
        }, cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task<int> RecoverStaleRunningAsync(CancellationToken ct = default)
    {
        const string sql = @"
UPDATE BacktestRuns
SET Status = 'Failed',
    ErrorMessage = N'Recovered: process restarted while run was in progress.'
WHERE Status = 'Running';";

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        return await conn.ExecuteAsync(new CommandDefinition(sql, cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task<BacktestRunRow?> GetByIdAsync(Guid runId, CancellationToken ct = default)
    {
        const string sql = @"
SELECT RunId, StrategyId, TunedId, InstrumentToken, BaseTimeframe, DataFrom, DataTo,
       RunMode, RunAt, RunBy, EngineVersion, DataSource, MetricsJson, ResultFilePath,
       Status, ErrorMessage
FROM BacktestRuns WHERE RunId = @RunId;";

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        return await conn.QuerySingleOrDefaultAsync<BacktestRunRow>(
            new CommandDefinition(sql, new { RunId = runId }, cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<BacktestRunRow>> GetRecentAsync(int limit, CancellationToken ct = default)
    {
        const string sql = @"
SELECT TOP (@Limit)
       RunId, StrategyId, TunedId, InstrumentToken, BaseTimeframe, DataFrom, DataTo,
       RunMode, RunAt, RunBy, EngineVersion, DataSource, MetricsJson, ResultFilePath,
       Status, ErrorMessage
FROM BacktestRuns ORDER BY RunAt DESC;";

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<BacktestRunRow>(
            new CommandDefinition(sql, new { Limit = limit }, cancellationToken: ct)).ConfigureAwait(false);
        return rows.AsList();
    }
}

public sealed class BacktestRunRow
{
    public Guid RunId { get; set; }
    public int StrategyId { get; set; }
    public int? TunedId { get; set; }
    public int InstrumentToken { get; set; }
    public string BaseTimeframe { get; set; } = string.Empty;
    public SysDateTime DataFrom { get; set; }
    public SysDateTime DataTo { get; set; }
    public string RunMode { get; set; } = string.Empty;
    public SysDateTime RunAt { get; set; }
    public string? RunBy { get; set; }
    public string EngineVersion { get; set; } = string.Empty;
    public string DataSource { get; set; } = string.Empty;
    public string? MetricsJson { get; set; }
    public string? ResultFilePath { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
}
