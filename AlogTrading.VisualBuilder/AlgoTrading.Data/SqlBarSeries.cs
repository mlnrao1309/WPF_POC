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
SELECT DISTINCT TimeStamp AS DateTime, [Open], [High], [Low], [Close], Volume, OI, RTRIM(TimeFrame) AS Timeframe
FROM dbo.vw_InstrumentBars
WHERE InstrumentToken = @token
  AND (UPPER(Timeframe) = UPPER(@tf) OR UPPER(Timeframe) = 'D')
  AND TimeStamp >= @from AND TimeStamp <= @to
ORDER BY Timeframe ASC, DateTime ASC;";

        const string symbolSql = "SELECT TOP (1) TradingSymbol FROM dbo.MarketWatch WHERE InstrumentToken = @token AND IsDeleted = 0 ORDER BY Idx;";

        using var conn = new SqlConnection(connectionString);
        conn.Open();

        string symbol = conn.QuerySingleOrDefault<string>(
            symbolSql, new { token = (int)instrumentToken }) ?? string.Empty;

        var requestedTimeframe = timeframe.Unit is TimeframeUnit.m or TimeframeUnit.H
            ? timeframe.ToMinutes().ToString(System.Globalization.CultureInfo.InvariantCulture)
            : timeframe.ToString();
        // CFCore stores exchange-local timestamps (IST); the backtest API uses UTC.
        var marketOffset = TimeSpan.FromMinutes(330);
        var rows = conn.Query<CandleRow>(barsSql, new
        {
            token = (int)instrumentToken,
            tf = requestedTimeframe,
            from = SysDateTime.SpecifyKind(fromUtc.Add(marketOffset), DateTimeKind.Unspecified),
            to = SysDateTime.SpecifyKind(toUtc.Add(marketOffset), DateTimeKind.Unspecified)
        }).ToArray();

        var requestedRows = new List<CandleRow>();
        var dailyRows = new List<CandleRow>();
        for (int i = 0; i < rows.Length; i++)
        {
            var row = rows[i];
            row.DateTime = SysDateTime.SpecifyKind(row.DateTime.Subtract(marketOffset), DateTimeKind.Utc);
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
