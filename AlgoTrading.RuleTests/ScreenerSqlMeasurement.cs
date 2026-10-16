using System.Data;
using System.Diagnostics;
using System.Text.Json;
using AlgoTrading.Screener.Dsl;
using Microsoft.Data.SqlClient;

internal sealed class ScreenerSqlMeasurement
{
    public async Task RunAsync(string settingsPath)
    {
        using JsonDocument document = JsonDocument.Parse(await File.ReadAllTextAsync(settingsPath));
        string connectionString = document.RootElement.GetProperty("ConnectionStrings")
            .GetProperty("MsSqlDatabase").GetString()
            ?? throw new InvalidOperationException("ConnectionStrings:MsSqlDatabase is required.");

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        Sample sample = await ReadSampleAsync(connection);

        QueryMeasurement legacy = await ReadAsync(connection, sample, bounded: false);
        QueryMeasurement bounded = await ReadAsync(connection, sample, bounded: true);
        decimal? legacySma = Indicators.Sma(Series.Num(legacy.Closes), 200).LastNum();
        decimal? boundedSma = Indicators.Sma(Series.Num(bounded.Closes), 200).LastNum();
        if (legacySma != boundedSma)
            throw new InvalidOperationException("Bounded SQL retrieval changed the SMA(200) result.");

        Console.WriteLine("InstrumentToken=" + sample.InstrumentToken);
        Console.WriteLine("TimeFrame=" + sample.TimeFrame);
        Console.WriteLine("Range=" + sample.FromInclusive.ToString("s") + ".." + sample.ToExclusive.ToString("s"));
        Console.WriteLine("LegacyRows=" + legacy.Closes.Length);
        Console.WriteLine("BoundedRows=" + bounded.Closes.Length);
        Console.WriteLine("RequiredRows=200");
        Console.WriteLine("LegacyElapsedMs=" + legacy.Elapsed.TotalMilliseconds.ToString("F2"));
        Console.WriteLine("BoundedElapsedMs=" + bounded.Elapsed.TotalMilliseconds.ToString("F2"));
        Console.WriteLine("SmaEqual=True");
    }

    private static async Task<Sample> ReadSampleAsync(SqlConnection connection)
    {
        const string sql = "SELECT TOP (1) InstrumentToken, TimeFrame, MIN(TimeStamp), "
            + "DATEADD(day, 1, MAX(TimeStamp)) FROM dbo.vw_InstrumentBars "
            + "GROUP BY InstrumentToken, TimeFrame HAVING COUNT_BIG(*) >= 200 ORDER BY COUNT_BIG(*) DESC;";
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = 60 };
        await using SqlDataReader reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow);
        if (!await reader.ReadAsync())
            throw new InvalidOperationException("No instrument/timeframe contains 200 rows.");
        return new Sample(reader.GetInt64(0), reader.GetString(1).Trim(), reader.GetDateTime(2), reader.GetDateTime(3));
    }

    private static async Task<QueryMeasurement> ReadAsync(SqlConnection connection, Sample sample, bool bounded)
    {
        string sql = bounded
            ? "SELECT [Close] FROM (SELECT [Close], TimeStamp, ROW_NUMBER() OVER "
                + "(PARTITION BY InstrumentToken ORDER BY TimeStamp DESC) AS rn "
                + "FROM dbo.vw_InstrumentBars WHERE InstrumentToken=@Token AND TimeFrame=@TimeFrame "
                + "AND TimeStamp>=@From AND TimeStamp<@To) ranked WHERE rn<=200 ORDER BY TimeStamp;"
            : "SELECT [Close] FROM dbo.vw_InstrumentBars WHERE InstrumentToken=@Token AND TimeFrame=@TimeFrame "
                + "AND TimeStamp>=@From AND TimeStamp<@To ORDER BY TimeStamp;";
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = 60 };
        command.Parameters.Add("@Token", SqlDbType.BigInt).Value = sample.InstrumentToken;
        command.Parameters.Add("@TimeFrame", SqlDbType.VarChar, 10).Value = sample.TimeFrame;
        command.Parameters.Add("@From", SqlDbType.DateTime).Value = sample.FromInclusive;
        command.Parameters.Add("@To", SqlDbType.DateTime).Value = sample.ToExclusive;

        var closes = new List<decimal>();
        var stopwatch = Stopwatch.StartNew();
        await using SqlDataReader reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess);
        while (await reader.ReadAsync()) closes.Add(reader.GetDecimal(0));
        stopwatch.Stop();
        return new QueryMeasurement(closes.Select(x => (decimal?)x).ToArray(), stopwatch.Elapsed);
    }

    private sealed record Sample(long InstrumentToken, string TimeFrame, DateTime FromInclusive,
        DateTime ToExclusive);
    private sealed record QueryMeasurement(decimal?[] Closes, TimeSpan Elapsed);
}
