using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlgoTrading.DataAccess.Etl;

public class RawEtlCandleDto
{
    public long InstrumentToken { get; set; }
    public string Symbol { get; set; } = string.Empty;
    public string Timeframe { get; set; } = string.Empty;
    public DateTime TimeStamp { get; set; }
    public decimal Open { get; set; }
    public decimal High { get; set; }
    public decimal Low { get; set; }
    public decimal Close { get; set; }
    public long? Volume { get; set; }
    public long? OI { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public bool IsFno { get; set; }
}

public class WarmupEtlRepository
{
    private readonly string _connectionString;

    public WarmupEtlRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<IEnumerable<RawEtlCandleDto>> FetchOneYearHistoryAsync(string timeframe)
    {
        using var connection = new SqlConnection(_connectionString);

        // Calculate 1 year back from today
        DateTime oneYearAgo = DateTime.Today.AddYears(-1);

        // Corrected SQL Query
        const string sql = @"
            SELECT 
                b.InstrumentToken, 
                b.TradingSymbol AS Symbol,
                b.TimeFrame AS Timeframe,
                b.TimeStamp,
                b.[Open], b.High, b.Low, b.[Close],
                b.Volume, b.OI,
                b.ExpiresOn AS ExpiryDate,
                CASE WHEN b.ExpiresOn IS NOT NULL THEN 1 ELSE 0 END AS IsFno
            FROM dbo.vw_InstrumentBars b
            WHERE b.TimeFrame = @TimeFrame 
              AND b.TimeStamp >= @OneYearAgo
              AND (b.ExpiresOn IS NULL OR b.ExpiresOn >= CAST(GETDATE() AS DATE))
            ORDER BY b.InstrumentToken, b.TimeStamp ASC;
        ";

        // Pass the parameters to Dapper
        return await connection.QueryAsync<RawEtlCandleDto>(sql, new
        {
            TimeFrame = timeframe,
            OneYearAgo = oneYearAgo
        });
    }
}