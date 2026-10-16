using AlgoTrading.DataAccess.Infrastructure.DatabaseContext;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System.Data;
using System.IO;
using System.Text;

namespace AlgoTrading.Screener.Repositories;

public sealed class ScreenerRepository : IScreenerRepository
{
    private const int TokenBatchSize = 500;
    private const string DailyPivotTimeframe = "D";
    private const string WeeklyPivotTimeframe = "W";
    private readonly ApplicationDbContext _context;

    public ScreenerRepository(ApplicationDbContext context)
        => _context = context ?? throw new ArgumentNullException(nameof(context));

    // =====================================================================
    // Universe
    // =====================================================================

    public async Task<IReadOnlyList<InstrumentRef>> GetUniverseAsync(
        string timeframe,
        DateTime fromInclusive,
        DateTime toExclusive,
        string? segment,
        string? exchange,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(timeframe);

        var q = _context.InstrumentBars.AsNoTracking()
            .Where(x => x.TimeFrame == timeframe
                     && x.TimeStamp >= fromInclusive
                     && x.TimeStamp <  toExclusive);

        if (!string.IsNullOrWhiteSpace(segment))
            q = q.Where(x => x.Segment == segment);

        if (!string.IsNullOrWhiteSpace(exchange))
            q = q.Where(x => x.Exchange == exchange);

        // One row per instrument.
        var rows = await q
            .Select(x => new
            {
                x.InstrumentToken,
                x.TradingSymbol,
                x.Segment,
                x.Exchange
            })
            .Distinct()
            .ToListAsync(cancellationToken);

        var seen   = new HashSet<long>();
        var result = new List<InstrumentRef>(rows.Count);

        foreach (var r in rows)
        {
            if (!seen.Add(r.InstrumentToken)) continue;

            result.Add(new InstrumentRef(
                r.InstrumentToken,
                r.TradingSymbol ?? r.InstrumentToken.ToString(),
                r.Segment?.Trim(),
                r.Exchange?.Trim()));
        }

        return result;
    }

    // =====================================================================
    // Bars + pivots
    // =====================================================================

    public async Task<IReadOnlyDictionary<long, IReadOnlyList<Bar>>> GetBarsBulkAsync(
     IReadOnlyCollection<long> instrumentTokens,
     string timeframe,
     DateTime fromInclusive,
     DateTime toExclusive,
     int? maxBarsPerInstrument = null,
     CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instrumentTokens);
        ArgumentException.ThrowIfNullOrWhiteSpace(timeframe);
        if (fromInclusive >= toExclusive)
            throw new ArgumentException("from must be earlier than to.");
        if (maxBarsPerInstrument is <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxBarsPerInstrument));

        var tokens = instrumentTokens.Distinct().ToArray();
        if (tokens.Length == 0)
            return new Dictionary<long, IReadOnlyList<Bar>>();

        // ---------------------------------------------------------------
        // Normalize timeframe once. All subsequent comparisons (bars query,
        // pivot queries, weekly lookups) use this normalized value.
        // ---------------------------------------------------------------
        string tf = timeframe.Trim().ToUpperInvariant();

        // ---------------------------------------------------------------
        // Query 1: bars
        // ---------------------------------------------------------------
        var barsByToken = new Dictionary<long, List<Bar>>(tokens.Length);

        for (int offset = 0; offset < tokens.Length; offset += TokenBatchSize)
        {
            var batch = tokens.Skip(offset).Take(TokenBatchSize).ToArray();

            var rows = maxBarsPerInstrument.HasValue
                ? await LoadLatestBarsAsync(batch, tf, fromInclusive, toExclusive,
                    maxBarsPerInstrument.Value, cancellationToken)
                : await _context.InstrumentBars.AsNoTracking()
                    .Where(x => batch.Contains(x.InstrumentToken)
                             && x.TimeFrame == tf
                             && x.TimeStamp >= fromInclusive
                             && x.TimeStamp < toExclusive)
                    .OrderBy(x => x.InstrumentToken).ThenBy(x => x.TimeStamp)
                    .ToListAsync(cancellationToken);

            foreach (var r in rows)
            {
                if (!barsByToken.TryGetValue(r.InstrumentToken, out var list))
                    barsByToken[r.InstrumentToken] = list = new List<Bar>();

                var ts = DateTime.SpecifyKind(r.TimeStamp, DateTimeKind.Unspecified);
                list.Add(new Bar(ts, r.Open, r.High, r.Low, r.Close,
                                 r.Volume ?? 0L, r.OI));
            }
        }

        if (barsByToken.Count == 0)
            return new Dictionary<long, IReadOnlyList<Bar>>();

        // ---------------------------------------------------------------
        // Pivot fetch window.
        //
        // Anchored to the EARLIEST bar in the batch, not the latest. Symbols
        // drop out of ingestion at different times; the earliest bar is the
        // one whose pivot row must still be fetchable. The upper bound is
        // one day past the latest bar so the newest trading day is included.
        //
        // TimeStamp convention (confirmed in the aggregation procs):
        //   Instruments_CalculatedPivots.TimeStamp == effective trading day.
        //   Pivot values are computed from T-1 OHLC. No forward shift needed.
        // ---------------------------------------------------------------
        var earliestBarDate = barsByToken.Values.Min(l => l[0].TimeStamp.Date);
        var latestBarDate = barsByToken.Values.Max(l => l[^1].TimeStamp.Date);

        var dailyFrom = earliestBarDate;
        var weeklyFrom = MondayOf(earliestBarDate);
        var toExclusivePivots = latestBarDate.AddDays(1);

        // ---------------------------------------------------------------
        // Query 2: pivots.
        // Timeframe strings for pivots: "D" and "W" — driven by usp_Aggregate
        // procs writing the inherited OHLC timeframe.
        // ---------------------------------------------------------------
        var dailyPivots = await LoadPivotsAsync(
            tokens, DailyPivotTimeframe, dailyFrom, toExclusivePivots, cancellationToken);

        var weeklyPivots = await LoadPivotsAsync(
            tokens, WeeklyPivotTimeframe, weeklyFrom, toExclusivePivots, cancellationToken);

        // ---------------------------------------------------------------
        // Enrich bars with pivots.
        // ---------------------------------------------------------------
        var result = new Dictionary<long, IReadOnlyList<Bar>>(barsByToken.Count);

        foreach (var kvp in barsByToken)
        {
            long token = kvp.Key;
            var bars = kvp.Value;

            dailyPivots.TryGetValue(token, out var dailyByDate);
            weeklyPivots.TryGetValue(token, out var weeklyByDate);

            var enriched = new Bar[bars.Count];

            for (int i = 0; i < bars.Count; i++)
            {
                var b = bars[i];
                var day = b.TimeStamp.Date;

                PivotRow? dp = null;
                dailyByDate?.TryGetValue(day, out dp);

                PivotRow? wp = null;
                if (weeklyByDate is not null)
                {
                    var monday = MondayOf(day);
                    weeklyByDate.TryGetValue(monday, out wp);
                }

                enriched[i] = b with
                {
                    Pivots = new Pivots(
                        PP: dp?.PP,
                        R1: dp?.R1, R2: dp?.R2, R3: dp?.R3, R4: dp?.R4, R5: dp?.R5,
                        S1: dp?.S1, S2: dp?.S2, S3: dp?.S3, S4: dp?.S4, S5: dp?.S5,

                        WeekPP: wp?.PP,
                        WeekR1: wp?.R1, WeekR2: wp?.R2, WeekR3: wp?.R3,
                        WeekR4: wp?.R4, WeekR5: wp?.R5,
                        WeekS1: wp?.S1, WeekS2: wp?.S2, WeekS3: wp?.S3,
                        WeekS4: wp?.S4, WeekS5: wp?.S5,

                        PPR1: dp?.PPR1 ?? false,
                        PPS1: dp?.PPS1 ?? false,
                        WeekPPR1: wp?.PPR1 ?? false,
                        WeekPPS1: wp?.PPS1 ?? false)
                };
            }

            result[token] = enriched;
        }

        return result;
    }

    private async Task<List<AlgoTrading.Models.MarketData.Entities.InstrumentBarView>> LoadLatestBarsAsync(
        long[] tokens, string timeframe, DateTime fromInclusive, DateTime toExclusive, int limit,
        CancellationToken cancellationToken)
    {
        var parameters = new List<SqlParameter>(tokens.Length + 4)
        {
            new("@TimeFrame", SqlDbType.VarChar, 10) { Value = timeframe },
            new("@FromInclusive", SqlDbType.DateTime) { Value = fromInclusive },
            new("@ToExclusive", SqlDbType.DateTime) { Value = toExclusive },
            new("@Limit", SqlDbType.Int) { Value = limit }
        };
        var tokenNames = new string[tokens.Length];
        for (int index = 0; index < tokens.Length; index++)
        {
            string name = "@Token" + index;
            tokenNames[index] = name;
            parameters.Add(new SqlParameter(name, SqlDbType.BigInt) { Value = tokens[index] });
        }

        var sql = new StringBuilder()
            .Append("SELECT InstrumentToken, [Open], High, Low, [Close], Volume, OI, TimeStamp, ")
            .Append("LastUpdated, TimeFrame, Segment, Exchange, ExpiresOn, TradingSymbol FROM (")
            .Append("SELECT vw.InstrumentToken, vw.[Open], vw.High, vw.Low, vw.[Close], vw.Volume, vw.OI, ")
            .Append("vw.TimeStamp, vw.LastUpdated, vw.TimeFrame, vw.Segment, vw.Exchange, vw.ExpiresOn, ")
            .Append("vw.TradingSymbol, ROW_NUMBER() OVER (PARTITION BY vw.InstrumentToken ORDER BY vw.TimeStamp DESC) AS rn ")
            .Append("FROM dbo.vw_InstrumentBars AS vw WHERE vw.InstrumentToken IN (")
            .AppendJoin(",", tokenNames)
            .Append(") AND vw.TimeFrame = @TimeFrame AND vw.TimeStamp >= @FromInclusive ")
            .Append("AND vw.TimeStamp < @ToExclusive) AS ranked WHERE rn <= @Limit ")
            .Append("ORDER BY InstrumentToken, TimeStamp")
            .ToString();

        return await _context.InstrumentBars.FromSqlRaw(sql, parameters.Cast<object>().ToArray())
            .AsNoTracking().ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Monday of the week containing <paramref name="date"/>.
    /// DATEFIRST-independent: DayOfWeek.Sunday=0, Monday=1, ..., Saturday=6.
    /// Formula maps Monday→0, Tuesday→1, ..., Sunday→6 and subtracts.
    /// </summary>
    private static DateTime MondayOf(DateTime date)
    {
        int daysSinceMonday = ((int)date.DayOfWeek + 6) % 7;
        return date.Date.AddDays(-daysSinceMonday);
    }
    // =====================================================================
    // Pivots helper
    // =====================================================================

    public async Task<FilterOptions> LoadFilterOptionsAsync()
    {
        var tfs = await this._context.InstrumentBars.AsNoTracking()
            .Select(x => x.TimeFrame)
            .Distinct()
            .OrderBy(x => x)
            .ToListAsync();

        tfs.ForEach((tf) => tf = tf.ToUpper());
        return new FilterOptions(tfs.ToList(), new List<string?>(), new List<string?>());

    }
    private async Task<Dictionary<long, Dictionary<DateTime, PivotRow>>> LoadPivotsAsync(
    long[] tokens,
    string timeframe,
    DateTime fromDate,
    DateTime toDate,
    CancellationToken cancellationToken)
    {
        // Normalize once. The DB stores varchar(10); we compare case-insensitively
        // because SQL Server's default collation is CI anyway, but pinning the
        // string here means a case change in the caller can't sneak through.
        string tf = timeframe.Trim().ToUpperInvariant();

        var result = new Dictionary<long, Dictionary<DateTime, PivotRow>>(tokens.Length);

        for (int offset = 0; offset < tokens.Length; offset += TokenBatchSize)
        {
            var batch = tokens.Skip(offset).Take(TokenBatchSize).ToArray();

            var rows = await _context.InstrumentsCalculatedPivots.AsNoTracking()
                .Where(p => batch.Contains(p.InstrumentToken)
                         && p.TimeFrame == tf
                         && p.TimeStamp >= fromDate
                         && p.TimeStamp < toDate).OrderBy(p => p.InstrumentToken).ThenBy(p => p.TimeStamp)
                .Select(p => new
                {
                    p.InstrumentToken,
                    p.TimeStamp,
                    p.PP,
                    p.R1,
                    p.R2,
                    p.R3,
                    p.R4,
                    p.R5,
                    p.S1,
                    p.S2,
                    p.S3,
                    p.S4,
                    p.S5,
                    p.PPR1,
                    p.PPS1
                })
                .ToListAsync(cancellationToken);

            foreach (var r in rows)
            {
                if (!result.TryGetValue(r.InstrumentToken, out var byDate))
                    result[r.InstrumentToken] = byDate = new Dictionary<DateTime, PivotRow>();

                // Deterministic duplicate handling: last-write-wins.
                // The PK on (InstrumentToken, TimeStamp, TimeFrame) makes true
                // duplicates impossible; this is defensive only.
                byDate[DateTime.SpecifyKind(r.TimeStamp.Date, DateTimeKind.Unspecified)]
                    = new PivotRow(
                        r.PP,
                        r.R1, r.R2, r.R3, r.R4, r.R5,
                        r.S1, r.S2, r.S3, r.S4, r.S5,
                        r.PPR1, r.PPS1);
            }
        }

        return result;
    }
    private sealed record PivotRow(
        decimal? PP,
        decimal? R1, decimal? R2, decimal? R3, decimal? R4, decimal? R5,
        decimal? S1, decimal? S2, decimal? S3, decimal? S4, decimal? S5, bool? PPR1, bool? PPS1 );

    public sealed record FilterOptions(List<string> Timeframes, List<string?> Segments, List<string?>? Exchanges);


}
