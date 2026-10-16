using AlgoTrading.Models;
using Microsoft.Data.SqlClient;
using System.Data;

namespace AlgoTrading.Repositories;

public sealed class InstrumentRepository : IInstrumentRepository
{
    private readonly string _connectionString;

    public InstrumentRepository(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _connectionString = settings.MsSqlDatabase;
    }

    public async Task<bool> ExistsAsync(long instrumentToken, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT CASE WHEN EXISTS
            (
                SELECT 1 FROM dbo.Instruments_EQ WHERE InstrumentToken = @InstrumentToken
                UNION ALL
                SELECT 1 FROM dbo.Instruments_FO WHERE InstrumentToken = @InstrumentToken
            ) THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END;
            """;
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@InstrumentToken", SqlDbType.BigInt).Value = instrumentToken;
        return (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
    }
}
