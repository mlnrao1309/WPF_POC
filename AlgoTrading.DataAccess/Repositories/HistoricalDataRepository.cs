using AlgoTrading.DataAccess.Infrastructure.DatabaseContext;
using AlgoTrading.Models;
using Microsoft.EntityFrameworkCore;
using System.Runtime.CompilerServices;

namespace AlgoTrading.Repositories;

public sealed class HistoricalDataRepository : IHistoricalDataRepository
{
    private const int TimestampQueryBatchSize = 1_000;
    private readonly ApplicationDbContext context;

    public HistoricalDataRepository(ApplicationDbContext context) =>
        this.context = context ?? throw new ArgumentNullException(nameof(context));

    public async Task<int> InsertBatchAsync(InstrumentCandleData batch, CancellationToken cancellationToken = default)
    {
        Candle[] candles = batch.Candles ?? throw new ArgumentException("Candles are required.", nameof(batch));
        if (candles.Length == 0) return 0;

        ValidateIdentity(batch.InstrumentToken, batch.TimeframeMinutes);
        var unique = new Dictionary<DateTime, Candle>(candles.Length);
        foreach (Candle candle in candles)
        {
            if (candle.InstrumentToken != batch.InstrumentToken || candle.TimeframeMinutes != batch.TimeframeMinutes)
                throw new ArgumentException("Every candle must match the batch instrument and timeframe.", nameof(batch));
            unique[MarketTimestamp.ToDatabase(candle.OpenedAt)] = candle;
        }

        DateTime[] timestamps = unique.Keys.ToArray();
        var existing = new HashSet<DateTime>();
        for (int offset = 0; offset < timestamps.Length; offset += TimestampQueryBatchSize)
        {
            DateTime[] queryTimestamps = timestamps.Skip(offset).Take(TimestampQueryBatchSize).ToArray();
            DateTime[] found = await this.context.InstrumentOhlc.AsNoTracking()
                .Where(x => x.InstrumentToken == batch.InstrumentToken
                    && x.TimeFrame == batch.TimeframeMinutes
                    && queryTimestamps.Contains(x.TimeStamp))
                .Select(x => x.TimeStamp)
                .ToArrayAsync(cancellationToken);
            existing.UnionWith(found);
        }

        List<InstrumentOhlc> inserts = unique.Where(x => !existing.Contains(x.Key))
            .Select(x => new InstrumentOhlc
            {
                InstrumentToken = batch.InstrumentToken,
                TimeFrame = batch.TimeframeMinutes,
                TimeStamp = x.Key,
                Open = x.Value.Open,
                High = x.Value.High,
                Low = x.Value.Low,
                Close = x.Value.Close,
                Volume = x.Value.Volume,
                OI = x.Value.OI
            }).ToList();
        if (inserts.Count == 0) return 0;

        await this.context.InstrumentOhlc.AddRangeAsync(inserts, cancellationToken);
        return await this.context.SaveChangesAsync(cancellationToken);
    }

    public async IAsyncEnumerable<InstrumentCandleData> GetPagesAsync(int instrumentToken, string timeframe,
        int pageSize, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ValidateIdentity(instrumentToken, timeframe);
        if (pageSize <= 0) throw new ArgumentOutOfRangeException(nameof(pageSize));

        DateTime? after = null;
        while (true)
        {
            IQueryable<InstrumentOhlc> query = this.context.InstrumentOhlc.AsNoTracking()
                .Where(x => x.InstrumentToken == instrumentToken && x.TimeFrame == timeframe);
            if (after.HasValue)
            {
                DateTime value = after.Value;
                query = query.Where(x => x.TimeStamp > value);
            }

            InstrumentOhlc[] rows = await query.OrderBy(x => x.TimeStamp).Take(pageSize)
                .ToArrayAsync(cancellationToken);
            if (rows.Length == 0) yield break;

            yield return ToInstrumentCandleData(instrumentToken, timeframe, rows);
            if (rows.Length < pageSize) yield break;
            after = rows[^1].TimeStamp;
        }
    }

    public async Task<InstrumentCandleData> GetByDateRangeAsync(int instrumentToken, string timeframe,
        DateTimeOffset fromInclusive, DateTimeOffset toExclusive, CancellationToken cancellationToken = default)
    {
        ValidateIdentity(instrumentToken, timeframe);
        if (fromInclusive >= toExclusive)
            throw new ArgumentException("The exclusive end must be later than the inclusive start.", nameof(toExclusive));

        DateTime from = MarketTimestamp.ToDatabase(fromInclusive);
        DateTime to = MarketTimestamp.ToDatabase(toExclusive);
        InstrumentOhlc[] rows = await this.context.InstrumentOhlc.AsNoTracking()
            .Where(x => x.InstrumentToken == instrumentToken && x.TimeFrame == timeframe
                && x.TimeStamp >= from && x.TimeStamp < to)
            .OrderBy(x => x.TimeStamp)
            .ToArrayAsync(cancellationToken);
        return ToInstrumentCandleData(instrumentToken, timeframe, rows);
    }

    private static InstrumentCandleData ToInstrumentCandleData(int instrumentToken, string timeframe,
        InstrumentOhlc[] rows)
    {
        var candles = new Candle[rows.Length];
        for (int index = 0; index < rows.Length; index++)
        {
            InstrumentOhlc row = rows[index];
            DateTime timestamp = DateTime.SpecifyKind(row.TimeStamp, DateTimeKind.Unspecified);
            candles[index] = new Candle(instrumentToken, timeframe, timestamp, row.Open, row.High, row.Low,
                row.Close, row.Volume, row.OI);
        }
        return new InstrumentCandleData(instrumentToken, timeframe, candles);
    }

    private static void ValidateIdentity(int instrumentToken, string timeframe)
    {
        if (instrumentToken <= 0) throw new ArgumentOutOfRangeException(nameof(instrumentToken));
        ArgumentException.ThrowIfNullOrWhiteSpace(timeframe);
        if (timeframe.Length > 10) throw new ArgumentOutOfRangeException(nameof(timeframe));
    }
}
