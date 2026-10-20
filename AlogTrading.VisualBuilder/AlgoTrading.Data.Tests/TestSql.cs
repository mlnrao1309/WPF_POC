using Dapper;
using Microsoft.Data.SqlClient;
using Xunit;
namespace AlgoTrading.Data.Tests;

internal static class TestSql
{
    private static readonly Lazy<string?> _cs = new(Probe, isThreadSafe: true);
    public static string? ConnectionString => _cs.Value;

    private static string? Probe()
    {
        string cs = Environment.GetEnvironmentVariable("ALGOTRADING_TEST_SQL")
            ?? @"Server=(localdb)\MSSQLLocalDB;Database=AlgoTradingTest;Integrated Security=true;Encrypt=false;TrustServerCertificate=true;";
        try
        {
            using var conn = new SqlConnection(cs);
            conn.Open();
            return cs;
        }
        catch { return null; }
    }

    public static async Task<int> EnsureStrategyAsync(string cs, string name)
    {
        const string selectSql = "SELECT StrategyId FROM Strategies WHERE Name = @name;";
        const string insertSql = @"
INSERT INTO Strategies (Name, JsonFilePath)
OUTPUT INSERTED.StrategyId
VALUES (@name, @path);";

        await using var conn = new SqlConnection(cs);
        await conn.OpenAsync();
        int? id = await conn.QuerySingleOrDefaultAsync<int?>(selectSql, new { name });
        if (id.HasValue) return id.Value;
        return await conn.QuerySingleAsync<int>(insertSql, new { name, path = $"strategies/{name}.json" });
    }

    public static async Task<int> EnsureInstrumentAsync(string cs, int token, string symbol)
    {
        const string selectSql = "SELECT Token FROM Instruments WHERE Token = @token;";
        const string insertSql = @"
INSERT INTO Instruments (Token, Symbol, DisplayName, InstrumentType, LotSize, TickSize)
VALUES (@token, @symbol, @symbol, 'Equity', 1, 0.05);";

        await using var conn = new SqlConnection(cs);
        await conn.OpenAsync();
        int? existing = await conn.QuerySingleOrDefaultAsync<int?>(selectSql, new { token });
        if (existing.HasValue) return existing.Value;
        await conn.ExecuteAsync(insertSql, new { token, symbol });
        return token;
    }
}

[CollectionDefinition("Sql", DisableParallelization = true)]
public sealed class SqlCollection : ICollectionFixture<object> { }