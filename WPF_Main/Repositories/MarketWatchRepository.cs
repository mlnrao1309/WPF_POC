using AlgoTrading.Models;
using AlgoTrading.Models.MarketData.Entities;
using Microsoft.Data.SqlClient;
using System.Data;

namespace AlgoTrading.Repositories;

public interface IMarketWatchRepository
{
    Task<IReadOnlyList<MarketWatch>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MarketWatch>> GetByWatchIdAsync(int watchId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MarketWatch>> GetByNameAsync(string name, CancellationToken cancellationToken = default);
    Task InsertAsync(MarketWatch marketWatch, CancellationToken cancellationToken = default);
    Task InsertManyAsync(IEnumerable<MarketWatch> marketWatches, CancellationToken cancellationToken = default);
    Task SoftDeleteWatchAsync(int watchId, string name, CancellationToken cancellationToken = default);
    Task SoftDeleteInstrumentAsync(int watchId, string name, long instrumentToken, CancellationToken cancellationToken = default);
    Task ToggleWatchActiveAsync(int watchId, string name, CancellationToken cancellationToken = default);
    Task ToggleInstrumentActiveAsync(int watchId, string name, long instrumentToken, CancellationToken cancellationToken = default);
    Task ToggleManyActiveAsync(IReadOnlyCollection<MarketWatch> targets, CancellationToken cancellationToken = default);
    Task ApplyChangesAsync(IReadOnlyCollection<MarketWatch> inserts, IReadOnlyCollection<MarketWatch> deletes, CancellationToken cancellationToken = default);
}

public sealed class MarketWatchRepository : IMarketWatchRepository
{
    private const int Timeout = 30;
    private readonly string _connectionString;

    public MarketWatchRepository(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _connectionString = settings.MsSqlDatabase;
    }

    public Task<IReadOnlyList<MarketWatch>> GetAllAsync(CancellationToken cancellationToken = default) =>
        ReadAsync("", static (_, _) => { }, cancellationToken);

    public Task<IReadOnlyList<MarketWatch>> GetByWatchIdAsync(int watchId, CancellationToken cancellationToken = default) =>
        ReadAsync(" AND WatchId = @WatchId", (command, _) => command.Parameters.Add("@WatchId", SqlDbType.Int).Value = watchId, cancellationToken);

    public Task<IReadOnlyList<MarketWatch>> GetByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return ReadAsync(" AND Name = @Name", (command, _) => command.Parameters.Add("@Name", SqlDbType.NVarChar, 200).Value = name, cancellationToken);
    }

    public Task InsertAsync(MarketWatch marketWatch, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(marketWatch);
        return InsertManyAsync([marketWatch], cancellationToken);
    }

    public async Task InsertManyAsync(IEnumerable<MarketWatch> marketWatches, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(marketWatches);
        MarketWatch[] rows = marketWatches.ToArray();
        if (rows.Length == 0) return;
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using SqlTransaction transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await InsertManyCoreAsync(connection, transaction, rows, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public Task SoftDeleteWatchAsync(int watchId, string name, CancellationToken cancellationToken = default) =>
        ExecuteAsync("WatchId = @WatchId AND Name = @Name", watchId, name, null, false, cancellationToken);

    public Task SoftDeleteInstrumentAsync(int watchId, string name, long instrumentToken, CancellationToken cancellationToken = default) =>
        ExecuteAsync("WatchId = @WatchId AND Name = @Name AND InstrumentToken = @InstrumentToken", watchId, name, instrumentToken, false, cancellationToken);

    public Task ToggleWatchActiveAsync(int watchId, string name, CancellationToken cancellationToken = default) =>
        ExecuteAsync("WatchId = @WatchId AND Name = @Name", watchId, name, null, true, cancellationToken);

    public Task ToggleInstrumentActiveAsync(int watchId, string name, long instrumentToken, CancellationToken cancellationToken = default) =>
        ExecuteAsync("WatchId = @WatchId AND Name = @Name AND InstrumentToken = @InstrumentToken", watchId, name, instrumentToken, true, cancellationToken);

    public async Task ToggleManyActiveAsync(IReadOnlyCollection<MarketWatch> targets, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(targets);
        if (targets.Count == 0) return;
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using SqlTransaction transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            const string create = "CREATE TABLE #MarketWatchToggle(WatchId int NOT NULL, Name nvarchar(200) NOT NULL, InstrumentToken bigint NOT NULL);";
            await using (var command = new SqlCommand(create, connection, transaction)) await command.ExecuteNonQueryAsync(cancellationToken);
            var table = new DataTable();
            table.Columns.Add("WatchId", typeof(int));
            table.Columns.Add("Name", typeof(string));
            table.Columns.Add("InstrumentToken", typeof(long));
            foreach (MarketWatch row in targets.DistinctBy(static row => (row.WatchId, row.Name, row.InstrumentToken)))
                table.Rows.Add(row.WatchId, row.Name, row.InstrumentToken);
            using (var bulk = new SqlBulkCopy(connection, SqlBulkCopyOptions.CheckConstraints, transaction))
            {
                bulk.DestinationTableName = "#MarketWatchToggle";
                bulk.BatchSize = table.Rows.Count;
                await bulk.WriteToServerAsync(table, cancellationToken);
            }
            const string update = """
                UPDATE target
                SET IsActive = CASE target.IsActive WHEN 1 THEN 0 ELSE 1 END,
                    ActiveInactiveOn = SYSUTCDATETIME(), LastUpdateTime = SYSUTCDATETIME()
                FROM dbo.MarketWatch AS target
                INNER JOIN #MarketWatchToggle AS selected
                    ON selected.WatchId = target.WatchId AND selected.Name = target.Name
                    AND selected.InstrumentToken = target.InstrumentToken
                WHERE target.IsDeleted = 0;
                """;
            await using (var command = new SqlCommand(update, connection, transaction)) await command.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task ApplyChangesAsync(IReadOnlyCollection<MarketWatch> inserts, IReadOnlyCollection<MarketWatch> deletes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inserts);
        ArgumentNullException.ThrowIfNull(deletes);
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using SqlTransaction transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await InsertManyCoreAsync(connection, transaction, inserts, cancellationToken);
            foreach (MarketWatch row in deletes)
            {
                const string sql = """
                    UPDATE dbo.MarketWatch
                    SET IsDeleted = 1, DeletedOn = SYSUTCDATETIME(), LastUpdateTime = SYSUTCDATETIME()
                    WHERE WatchId = @WatchId AND Name = @Name AND InstrumentToken = @InstrumentToken AND IsDeleted = 0;
                    """;
                await using var command = CreateCommand(sql, connection, transaction, row.WatchId, row.Name, row.InstrumentToken);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private async Task<IReadOnlyList<MarketWatch>> ReadAsync(string predicate, Action<SqlCommand, object?> bind, CancellationToken cancellationToken)
    {
        string sql = """
            SELECT Idx, WatchId, Name, InstrumentToken, IsDeleted, IsActive,
                   InsertedOn, DeletedOn, ActiveInactiveOn, LastUpdateTime
            FROM dbo.MarketWatch
            WHERE IsDeleted = 0
            """ + predicate + " ORDER BY WatchId, Name, InstrumentToken;";
        List<MarketWatch> rows = [];
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = Timeout };
        bind(command, null);
        await using SqlDataReader reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess, cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new MarketWatch
            {
                Idx = reader.GetInt64(0), WatchId = reader.GetInt32(1), Name = reader.GetString(2),
                InstrumentToken = reader.GetInt64(3), IsDeleted = reader.GetBoolean(4), IsActive = reader.GetBoolean(5),
                InsertedOn = reader.GetDateTime(6), DeletedOn = reader.IsDBNull(7) ? null : reader.GetDateTime(7),
                ActiveInactiveOn = reader.IsDBNull(8) ? null : reader.GetDateTime(8),
                LastUpdateTime = reader.IsDBNull(9) ? null : reader.GetDateTime(9)
            });
        }
        return rows;
    }

    private async Task ExecuteAsync(string predicate, int watchId, string name, long? instrumentToken, bool toggle, CancellationToken cancellationToken)
    {
        string set = toggle
            ? "IsActive = CASE IsActive WHEN 1 THEN 0 ELSE 1 END, ActiveInactiveOn = SYSUTCDATETIME(), LastUpdateTime = SYSUTCDATETIME()"
            : "IsDeleted = 1, DeletedOn = SYSUTCDATETIME(), LastUpdateTime = SYSUTCDATETIME()";
        string sql = $"UPDATE dbo.MarketWatch SET {set} WHERE {predicate} AND IsDeleted = 0;";
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = CreateCommand(sql, connection, null, watchId, name, instrumentToken);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertManyCoreAsync(SqlConnection connection, SqlTransaction transaction, IEnumerable<MarketWatch> source, CancellationToken cancellationToken)
    {
        MarketWatch[] rows = source.ToArray();
        if (rows.Length == 0) return;
        const string create = "CREATE TABLE #MarketWatchInsert(WatchId int NOT NULL, Name nvarchar(200) NOT NULL, InstrumentToken bigint NOT NULL);";
        await using (var command = new SqlCommand(create, connection, transaction)) await command.ExecuteNonQueryAsync(cancellationToken);
        var table = new DataTable();
        table.Columns.Add("WatchId", typeof(int));
        table.Columns.Add("Name", typeof(string));
        table.Columns.Add("InstrumentToken", typeof(long));
        foreach (MarketWatch row in rows) table.Rows.Add(row.WatchId, row.Name, row.InstrumentToken);
        using (var bulk = new SqlBulkCopy(connection, SqlBulkCopyOptions.CheckConstraints, transaction))
        {
            bulk.DestinationTableName = "#MarketWatchInsert";
            bulk.BatchSize = rows.Length;
            await bulk.WriteToServerAsync(table, cancellationToken);
        }
        const string insert = """
            INSERT dbo.MarketWatch(WatchId, Name, InstrumentToken, IsDeleted, IsActive, InsertedOn)
            SELECT DISTINCT source.WatchId, source.Name, source.InstrumentToken, 0, 0, SYSUTCDATETIME()
            FROM #MarketWatchInsert AS source
            WHERE NOT EXISTS
            (
                SELECT 1 FROM dbo.MarketWatch AS target
                WHERE target.WatchId = source.WatchId AND target.Name = source.Name
                  AND target.InstrumentToken = source.InstrumentToken
            );
            """;
        await using (var command = new SqlCommand(insert, connection, transaction)) await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static SqlCommand CreateCommand(string sql, SqlConnection connection, SqlTransaction? transaction, int watchId, string name, long? instrumentToken)
    {
        var command = new SqlCommand(sql, connection, transaction) { CommandTimeout = Timeout };
        command.Parameters.Add("@WatchId", SqlDbType.Int).Value = watchId;
        command.Parameters.Add("@Name", SqlDbType.NVarChar, 200).Value = name;
        if (instrumentToken.HasValue) command.Parameters.Add("@InstrumentToken", SqlDbType.BigInt).Value = instrumentToken.Value;
        return command;
    }
}
