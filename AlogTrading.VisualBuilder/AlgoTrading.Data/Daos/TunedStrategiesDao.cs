using AlgoTrading.Models;
using Dapper;
using Microsoft.Data.SqlClient;
using SysDateTime = System.DateTime;

namespace AlgoTrading.Data.Daos;

public sealed class TunedStrategiesDao
{
    private readonly string _connectionString;

    public TunedStrategiesDao(string connectionString)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
    }

    public async Task<int> SaveAsync(
        int strategyId, uint instrumentToken, Timeframe timeframe,
        string parametersJson, string jsonFilePath, string? tunedBy,
        Guid? backtestRunId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parametersJson);
        ArgumentException.ThrowIfNullOrWhiteSpace(jsonFilePath);

        const string selectSql = @"
SELECT TunedId FROM TunedStrategies
WHERE StrategyId = @StrategyId AND InstrumentToken = @InstrumentToken AND Timeframe = @Timeframe;";
        const string updateSql = @"
UPDATE TunedStrategies
SET ParametersJson = @ParametersJson, JsonFilePath = @JsonFilePath,
    TunedAt = SYSUTCDATETIME(), TunedBy = @TunedBy, BacktestRunId = @BacktestRunId
WHERE TunedId = @TunedId;";
        const string insertSql = @"
INSERT INTO TunedStrategies
    (StrategyId, InstrumentToken, Timeframe, ParametersJson, JsonFilePath, TunedBy, BacktestRunId)
OUTPUT INSERTED.TunedId
VALUES
    (@StrategyId, @InstrumentToken, @Timeframe, @ParametersJson, @JsonFilePath, @TunedBy, @BacktestRunId);";

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct).ConfigureAwait(false);

        int? existing = await conn.QuerySingleOrDefaultAsync<int?>(
            new CommandDefinition(selectSql, new
            {
                StrategyId = strategyId,
                InstrumentToken = (int)instrumentToken,
                Timeframe = timeframe.ToString()
            }, tx, cancellationToken: ct)).ConfigureAwait(false);

        int id;
        if (existing.HasValue)
        {
            await conn.ExecuteAsync(new CommandDefinition(updateSql, new
            {
                ParametersJson = parametersJson, JsonFilePath = jsonFilePath,
                TunedBy = tunedBy, BacktestRunId = backtestRunId, TunedId = existing.Value
            }, tx, cancellationToken: ct)).ConfigureAwait(false);
            id = existing.Value;
        }
        else
        {
            id = await conn.QuerySingleAsync<int>(
                new CommandDefinition(insertSql, new
                {
                    StrategyId = strategyId,
                    InstrumentToken = (int)instrumentToken,
                    Timeframe = timeframe.ToString(),
                    ParametersJson = parametersJson,
                    JsonFilePath = jsonFilePath,
                    TunedBy = tunedBy,
                    BacktestRunId = backtestRunId
                }, tx, cancellationToken: ct)).ConfigureAwait(false);
        }

        await tx.CommitAsync(ct).ConfigureAwait(false);
        return id;
    }

    public async Task<TunedStrategyRow?> GetByKeyAsync(int strategyId, uint instrumentToken, Timeframe timeframe, CancellationToken ct = default)
    {
        const string sql = @"
SELECT TunedId, StrategyId, InstrumentToken, Timeframe, ParametersJson,
       JsonFilePath, TunedAt, TunedBy, BacktestRunId
FROM TunedStrategies
WHERE StrategyId = @StrategyId AND InstrumentToken = @InstrumentToken AND Timeframe = @Timeframe;";

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        return await conn.QuerySingleOrDefaultAsync<TunedStrategyRow>(
            new CommandDefinition(sql, new
            {
                StrategyId = strategyId,
                InstrumentToken = (int)instrumentToken,
                Timeframe = timeframe.ToString()
            }, cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task<TunedStrategyRow?> GetByIdAsync(int tunedId, CancellationToken ct = default)
    {
        const string sql = @"
SELECT TunedId, StrategyId, InstrumentToken, Timeframe, ParametersJson,
       JsonFilePath, TunedAt, TunedBy, BacktestRunId
FROM TunedStrategies WHERE TunedId = @TunedId;";

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        return await conn.QuerySingleOrDefaultAsync<TunedStrategyRow>(
            new CommandDefinition(sql, new { TunedId = tunedId }, cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task<bool> DeleteAsync(int tunedId, CancellationToken ct = default)
    {
        const string sql = "DELETE FROM TunedStrategies WHERE TunedId = @TunedId;";

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        try
        {
            int rows = await conn.ExecuteAsync(
                new CommandDefinition(sql, new { TunedId = tunedId }, cancellationToken: ct)).ConfigureAwait(false);
            return rows > 0;
        }
        catch (SqlException ex) when (ex.Number == 547)
        {
            return false;
        }
    }
}

public sealed class TunedStrategyRow
{
    public int TunedId { get; set; }
    public int StrategyId { get; set; }
    public int InstrumentToken { get; set; }
    public string Timeframe { get; set; } = string.Empty;
    public string ParametersJson { get; set; } = string.Empty;
    public string JsonFilePath { get; set; } = string.Empty;
    public SysDateTime TunedAt { get; set; }
    public string? TunedBy { get; set; }
    public Guid? BacktestRunId { get; set; }
}