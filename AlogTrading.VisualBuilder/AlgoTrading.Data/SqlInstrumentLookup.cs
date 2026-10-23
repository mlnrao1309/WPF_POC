using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using AlgoTrading.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlgoTrading.Data;

public sealed class SqlInstrumentLookup : IInstrumentLookup
{
    private readonly string _connectionString;
    private readonly ConcurrentDictionary<uint, InstrumentInfo?> _cache = new();

    public SqlInstrumentLookup(string connectionString)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
    }

    public bool TryGet(uint token, [NotNullWhen(true)] out InstrumentInfo info)
    {
        var cached = _cache.GetOrAdd(token, static (t, cs) => QueryOne(cs, t), _connectionString);
        if (cached is null) { info = null!; return false; }
        info = cached;
        return true;
    }

    private static InstrumentInfo? QueryOne(string cs, uint token)
    {
        const string sql = @"
SELECT mw.InstrumentToken as Token
	   ,mw.TradingSymbol as Symbol
	   ,mw.TradingSymbol as DisplayName
	   ,mw.Segment as InstrumentType
	   ,1 as LotSize
	   ,0.05 as TickSize
 FROM MarketWatch mw where mw.InstrumentToken = @token";
        using var conn = new SqlConnection(cs);
        var row = conn.QuerySingleOrDefault<InstrumentRow>(sql, new { token = (int)token });
        if (row is null) return null;
        return new InstrumentInfo
        {
            Token = (uint)row.Token,
            Symbol = row.Symbol ?? string.Empty,
            DisplayName = row.DisplayName ?? string.Empty,
            InstrumentType = ParseType(row.InstrumentType),
            LotSize = row.LotSize,
            TickSize = row.TickSize
        };
    }

    private static InstrumentType ParseType(string? s)
    {
        if (string.IsNullOrEmpty(s)) return InstrumentType.Equity;
        return Enum.TryParse<InstrumentType>(s, ignoreCase: true, out var t) ? t : InstrumentType.Equity;
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