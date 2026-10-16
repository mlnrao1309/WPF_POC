using AlgoTrading.Models;
using AlgoTrading.Models.MarketData.Entities;
using AlgoTrading.Repositories;
using Microsoft.Data.SqlClient;
using System.Data;

namespace AlgoTrading.Services;

public readonly record struct MarketWatchTarget(int WatchId, string Name, long InstrumentToken);

public sealed record MarketWatchDataRow(
    int WatchId,
    string Name,
    long InstrumentToken,
    string TradingSymbol,
    string Exchange,
    string InstrumentType,
    bool IsActive);

public interface IMarketWatchDataService
{
    Task<IReadOnlyList<MarketWatchDataRow>> LoadAsync(CancellationToken cancellationToken = default);
    Task ToggleActiveAsync(IReadOnlyCollection<MarketWatchTarget> targets, CancellationToken cancellationToken = default);
}

public sealed class SqlMarketWatchDataService : IMarketWatchDataService
{
    private const int CommandTimeoutSeconds = 30;
    private readonly string _connectionString;
    private readonly IMarketWatchRepository _repository;

    public SqlMarketWatchDataService(AppSettings settings, IMarketWatchRepository repository)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _connectionString = settings.MsSqlDatabase;
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<IReadOnlyList<MarketWatchDataRow>> LoadAsync(CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        const string sql = """
            SELECT mw.WatchId,
                   mw.Name,
                   mw.InstrumentToken,
                   COALESCE(eq.TradingSymbol, fo.TradingSymbol, N'') AS TradingSymbol,
                   COALESCE(eq.Exchange, fo.Exchange, N'') AS Exchange,
                   COALESCE(eq.Segment, fo.Segment, N'') AS InstrumentType,
                   mw.IsActive
            FROM dbo.MarketWatch AS mw
            LEFT JOIN dbo.Instruments_EQ AS eq
                ON eq.InstrumentToken = mw.InstrumentToken
            LEFT JOIN dbo.Instruments_FO AS fo
                ON fo.InstrumentToken = mw.InstrumentToken
            WHERE mw.IsDeleted = 0
            ORDER BY mw.Name, mw.WatchId, TradingSymbol;
            """;

        List<MarketWatchDataRow> rows = [];
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = CommandTimeoutSeconds };
        await using SqlDataReader reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess, cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new MarketWatchDataRow(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetInt64(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetBoolean(6)));
        }

        return rows;
    }

    public async Task ToggleActiveAsync(IReadOnlyCollection<MarketWatchTarget> targets, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(targets);
        if (targets.Count == 0) return;
        EnsureConfigured();

        MarketWatch[] rows = targets.Distinct().Select(static target => new MarketWatch
        {
            WatchId = target.WatchId,
            Name = target.Name,
            InstrumentToken = target.InstrumentToken
        }).ToArray();
        await _repository.ToggleManyActiveAsync(rows, cancellationToken);
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_connectionString))
            throw new InvalidOperationException("The SQL Server connection string is not configured.");
    }
}
