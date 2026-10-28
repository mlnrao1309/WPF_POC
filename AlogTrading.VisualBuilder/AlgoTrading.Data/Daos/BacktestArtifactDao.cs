using Dapper;
using Microsoft.Data.SqlClient;
using System.Security.Cryptography;
using System.Text;

namespace AlgoTrading.Data.Daos;

public sealed class BacktestArtifactDao
{
    private readonly string _connectionString;

    public BacktestArtifactDao(string connectionString)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
    }

    public async Task InsertArtifactsAsync(Guid runId, IEnumerable<dynamic> artifacts)
    {
        var list = artifacts.ToList();
        if (list.Count == 0) return;

        using var conn = new SqlConnection(_connectionString);

        const string sql = @"
            INSERT INTO dbo.StrategyBacktestArtifact (
                RunId, ArtifactKind, ArtifactName, PayloadFormat, SchemaVersion, 
                PayloadText, RecordCount, PayloadSha256
            )
            VALUES (
                @RunId, @ArtifactKind, @ArtifactName, 'JSON', 1, 
                @PayloadText, @RecordCount, @PayloadSha256
            )";

        var parameters = new List<object>();
        foreach (var a in list)
        {
            string payloadText = (string)a.PayloadText;
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(payloadText));

            parameters.Add(new
            {
                RunId = runId,
                ArtifactKind = (string)a.ArtifactKind,
                ArtifactName = (string)a.ArtifactName,
                PayloadText = payloadText,
                RecordCount = (long)a.RecordCount,
                PayloadSha256 = hash
            });
        }

        await conn.ExecuteAsync(sql, parameters);
    }
}