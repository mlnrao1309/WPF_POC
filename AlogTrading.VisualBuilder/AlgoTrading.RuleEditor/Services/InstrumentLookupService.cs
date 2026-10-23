using Dapper;
using Microsoft.Data.SqlClient;

namespace AlgoTrading.RuleEditor.Services;

public sealed class InstrumentInfoDto
{
    public uint Token { get; init; }
    public string Symbol { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string InstrumentType { get; init; } = "Equity";
    public int LotSize { get; init; }
    public decimal TickSize { get; init; }
}

/// <summary>
/// Minimal, editor-owned SQL lookup for Instruments (Section 8.15).
/// Uses Dapper. Does not reference AlgoTrading.Data.
/// </summary>
public sealed class InstrumentLookupService
{
    private const string Sql =
        @"SELECT mw.InstrumentToken as Token
       , mw.TradingSymbol as Symbol
	   ,mw.TradingSymbol as DisplayName
	   ,mw.Segment as InstrumentType
	   ,1 as LotSize
	   ,0.05 as TickSize
 FROM MarketWatch mw where mw.InstrumentToken = @token";

    private readonly string _connectionString;

    public InstrumentLookupService(string? connectionString)
    {
        _connectionString = connectionString ?? "";
    }

    public bool IsAvailable => !string.IsNullOrWhiteSpace(_connectionString);

    public bool TryGet(uint token, out InstrumentInfoDto info)
    {
        info = null!;
        if (!IsAvailable || token == 0) return false;

        try
        {
            using var conn = new SqlConnection(_connectionString);
            var row = conn.QueryFirstOrDefault<InstrumentRow>(Sql, new { token = (int)token });
            if (row is null) return false;

            info = new InstrumentInfoDto
            {
                Token = (uint)row.Token,
                Symbol = row.Symbol ?? "",
                DisplayName = row.DisplayName ?? row.Symbol ?? "",
                InstrumentType = row.InstrumentType ?? "Equity",
                LotSize = row.LotSize,
                TickSize = row.TickSize
            };
            return true;
        }
        catch
        {
            return false;
        }
    }

    public IReadOnlyList<InstrumentInfoDto> Search(string? prefix, int limit = 100)
    {
        if (!IsAvailable) return Array.Empty<InstrumentInfoDto>();
        var like = "%" + (prefix ?? "") + "%";
        const string q = @"SELECT Top (@limit) Token, Symbol, DisplayName, InstrumentType, LotSize, TickSize  from (SELECT mw.InstrumentToken as Token
	   ,mw.TradingSymbol as Symbol
	   ,mw.TradingSymbol as DisplayName
	   ,mw.Segment as InstrumentType
	   ,1 as LotSize
	   ,0.05 as TickSize
 FROM MarketWatch mw where mw.InstrumentToken = any (
SELECT ib.InstrumentToken
  FROM [Algo_Trading_CFCore].[dbo].vw_InstrumentBars ib
  ) )b where b.Symbol like @like OR DisplayName like @like
  Order by Symbol";
        try
        {
            using var conn = new SqlConnection(_connectionString);
            var rows = conn.Query<InstrumentRow>(q, new { like, limit }).ToList();
            var result = new List<InstrumentInfoDto>(rows.Count);
            foreach (var r in rows)
            {
                result.Add(new InstrumentInfoDto
                {
                    Token = (uint)r.Token,
                    Symbol = r.Symbol ?? "",
                    DisplayName = r.DisplayName ?? r.Symbol ?? "",
                    InstrumentType = r.InstrumentType ?? "Equity",
                    LotSize = r.LotSize,
                    TickSize = r.TickSize
                });
            }
            return result;
        }
        catch
        {
            return Array.Empty<InstrumentInfoDto>();
        }
    }

    private sealed class InstrumentRow
    {
        public int Token { get; set; }
        public string? Symbol { get; set; }
        public string? DisplayName { get; set; }
        public string? InstrumentType { get; set; }
        public int LotSize { get; set; }
        public decimal TickSize { get; set; }
    }
}