using Dapper;
using Microsoft.Data.SqlClient;
using SysDateTime = System.DateTime;

namespace AlgoTrading.Data.Daos;

public sealed class StrategiesDao
{
    private readonly string _connectionString;

    public StrategiesDao(string connectionString)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
    }

    public async Task<int> SaveAsync(string name, string? description, string jsonFilePath, string? createdBy, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(jsonFilePath);

        const string selectSql = "SELECT StrategyId FROM Strategies WHERE Name = @Name;";
        const string updateSql = @"
UPDATE Strategies SET Description = @Description, JsonFilePath = @JsonFilePath
WHERE StrategyId = @StrategyId;";
        const string insertSql = @"
INSERT INTO Strategies (Name, Description, JsonFilePath, CreatedBy)
OUTPUT INSERTED.StrategyId
VALUES (@Name, @Description, @JsonFilePath, @CreatedBy);";

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct).ConfigureAwait(false);

        int? existing = await conn.QuerySingleOrDefaultAsync<int?>(
            new CommandDefinition(selectSql, new { Name = name }, tx, cancellationToken: ct)).ConfigureAwait(false);

        int id;
        if (existing.HasValue)
        {
            await conn.ExecuteAsync(new CommandDefinition(updateSql, new
            {
                Description = description, JsonFilePath = jsonFilePath, StrategyId = existing.Value
            }, tx, cancellationToken: ct)).ConfigureAwait(false);
            id = existing.Value;
        }
        else
        {
            id = await conn.QuerySingleAsync<int>(
                new CommandDefinition(insertSql, new
                {
                    Name = name, Description = description, JsonFilePath = jsonFilePath, CreatedBy = createdBy
                }, tx, cancellationToken: ct)).ConfigureAwait(false);
        }

        await tx.CommitAsync(ct).ConfigureAwait(false);
        return id;
    }

    public async Task<StrategyRow?> GetByNameAsync(string name, CancellationToken ct = default)
    {
        const string sql = @"
SELECT StrategyId, Name, Description, JsonFilePath, CreatedAt, CreatedBy
FROM Strategies WHERE Name = @Name;";

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        return await conn.QuerySingleOrDefaultAsync<StrategyRow>(
            new CommandDefinition(sql, new { Name = name }, cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task<StrategyRow?> GetByIdAsync(int strategyId, CancellationToken ct = default)
    {
        const string sql = @"
SELECT StrategyId, Name, Description, JsonFilePath, CreatedAt, CreatedBy
FROM Strategies WHERE StrategyId = @StrategyId;";

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        return await conn.QuerySingleOrDefaultAsync<StrategyRow>(
            new CommandDefinition(sql, new { StrategyId = strategyId }, cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task<bool> DeleteAsync(int strategyId, CancellationToken ct = default)
    {
        const string sql = "DELETE FROM Strategies WHERE StrategyId = @StrategyId;";

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        try
        {
            int rows = await conn.ExecuteAsync(
                new CommandDefinition(sql, new { StrategyId = strategyId }, cancellationToken: ct)).ConfigureAwait(false);
            return rows > 0;
        }
        catch (SqlException ex) when (ex.Number == 547)
        {
            return false;
        }
    }
}

public sealed class StrategyRow
{
    public int StrategyId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string JsonFilePath { get; set; } = string.Empty;
    public SysDateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
}