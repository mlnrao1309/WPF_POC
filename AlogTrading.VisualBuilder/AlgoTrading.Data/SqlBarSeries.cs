using AlgoTrading.Models;
using Dapper;
using Microsoft.Data.SqlClient;
using SysDateTime = System.DateTime;

namespace AlgoTrading.Data;

public sealed class SqlBarSeries : IBarSeries
{
    public uint InstrumentToken { get; }
    public string Symbol { get; }
    public Timeframe Timeframe { get; }
    public SysDateTime From { get; }
    public SysDateTime To { get; }
    public int Count { get; }

    public ReadOnlyMemory<decimal> Open { get; }
    public ReadOnlyMemory<decimal> High { get; }
    public ReadOnlyMemory<decimal> Low { get; }
    public ReadOnlyMemory<decimal> Close { get; }
    public ReadOnlyMemory<decimal> Volume { get; }
    public ReadOnlyMemory<decimal> OI { get; }
    public ReadOnlyMemory<SysDateTime> DateTime { get; }
    public IBarSeries? DailySeries { get; private set; }

    private SqlBarSeries(
        uint instrumentToken, string symbol, Timeframe timeframe,
        SysDateTime[] dateTime, decimal[] open, decimal[] high, decimal[] low,
        decimal[] close, decimal[] volume, decimal[] oi)
    {
        InstrumentToken = instrumentToken;
        Symbol = symbol;
        Timeframe = timeframe;
        DateTime = dateTime;
        Open = open;
        High = high;
        Low = low;
        Close = close;
        Volume = volume;
        OI = oi;
        Count = dateTime.Length;
        From = dateTime.Length > 0 ? dateTime[0] : default;
        To = dateTime.Length > 0 ? dateTime[^1] : default;
    }

    public static SqlBarSeries Load(
        string connectionString, uint instrumentToken, Timeframe timeframe,
        SysDateTime fromUtc, SysDateTime toUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        const string barsSql = @"
SELECT DateTime, [Open], [High], [Low], [Close], Volume, OI, Timeframe
FROM Candles
WHERE InstrumentToken = @token
  AND (UPPER(Timeframe) = UPPER(@tf) OR UPPER(Timeframe) = 'D')
  AND DateTime >= @from AND DateTime <= @to
ORDER BY Timeframe ASC, DateTime ASC;";

        const string symbolSql = "SELECT Symbol FROM Instruments WHERE Token = @token;";

        using var conn = new SqlConnection(connectionString);
        conn.Open();

        string symbol = conn.QuerySingleOrDefault<string>(
            symbolSql, new { token = (int)instrumentToken }) ?? string.Empty;

        var rows = conn.Query<CandleRow>(barsSql, new
        {
            token = (int)instrumentToken,
            tf = timeframe.ToString(),
            from = fromUtc,
            to = toUtc
        }).ToArray();

        var requestedRows = new List<CandleRow>();
        var dailyRows = new List<CandleRow>();
        var requestedTimeframe = timeframe.ToString();
        for (int i = 0; i < rows.Length; i++)
        {
            var row = rows[i];
            if (string.Equals(row.Timeframe, requestedTimeframe, StringComparison.OrdinalIgnoreCase))
                requestedRows.Add(row);
            if (string.Equals(row.Timeframe, "D", StringComparison.OrdinalIgnoreCase))
                dailyRows.Add(row);
        }

        var series = CreateSeries(instrumentToken, symbol, timeframe, requestedRows);
        if (timeframe.Equals(Timeframe.Parse("D")))
            series.DailySeries = series;
        else if (dailyRows.Count > 0)
            series.DailySeries = CreateSeries(
                instrumentToken, symbol, Timeframe.Parse("D"), dailyRows);

        return series;
    }

    private static SqlBarSeries CreateSeries(
        uint instrumentToken, string symbol, Timeframe timeframe,
        IReadOnlyList<CandleRow> rows)
    {
        int n = rows.Count;
        var dtArr = new SysDateTime[n];
        var oArr = new decimal[n];
        var hArr = new decimal[n];
        var lArr = new decimal[n];
        var cArr = new decimal[n];
        var vArr = new decimal[n];
        var oiArr = new decimal[n];

        for (int i = 0; i < n; i++)
        {
            var r = rows[i];
            dtArr[i] = r.DateTime;
            oArr[i] = r.Open;
            hArr[i] = r.High;
            lArr[i] = r.Low;
            cArr[i] = r.Close;
            vArr[i] = r.Volume;
            oiArr[i] = r.OI;
        }

        return new SqlBarSeries(
            instrumentToken, symbol, timeframe,
            dtArr, oArr, hArr, lArr, cArr, vArr, oiArr);
    }

    private sealed class CandleRow
    {
        public SysDateTime DateTime { get; set; }
        public decimal Open { get; set; }
        public decimal High { get; set; }
        public decimal Low { get; set; }
        public decimal Close { get; set; }
        public decimal Volume { get; set; }
        public decimal OI { get; set; }
        public string Timeframe { get; set; } = "";
    }
}
