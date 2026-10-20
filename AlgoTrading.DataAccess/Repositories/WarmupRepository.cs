using System.Data;
using Microsoft.Data.SqlClient;
using Dapper;
using Stage1Scanner.Core.Market;
using Stage1Scanner.Core.Indicators;
using System.Linq;
using System.Diagnostics;
namespace AlgoTrading.DataAccess.Repositories;

public class WarmupRepository : IWarmupRepository
{
    private readonly string _connectionString;

    public WarmupRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<IEnumerable<DisqualifiedInstrumentDto>> GetDisqualifiedInstrumentsAsync()
    {
        using var connection = new SqlConnection(_connectionString);
        return await connection.QueryAsync<DisqualifiedInstrumentDto>(
            "usp_GetDisqualifiedInstruments", commandType: CommandType.StoredProcedure);
    }

    public async Task<Dictionary<long, Instruments>> GetWarmupSnapshotAsync(short weeksNeeded)
    {
        using var connection = new SqlConnection(_connectionString);
        using var multi = await connection.QueryMultipleAsync(
            "usp_GetWarmupSnapshot", new { WeeksNeeded = weeksNeeded }, commandType: CommandType.StoredProcedure);

        var pivots = await multi.ReadAsync<PivotRowDto>();
        var raw60Min = await multi.ReadAsync<RawCandleDto>();
        var raw15Min = await multi.ReadAsync<RawCandleDto>();

        var x = raw60Min.Where(x => x.TradingSymbol.Contains("SENSEX") && x.TimeFrame.Contains("60"));

        var instrumentDict = new Dictionary<long, Instruments>();

        Instruments GetOrRegister(long token, string symbol)
        {
            if (!instrumentDict.TryGetValue(token, out var inst))
            {
                inst = new Instruments(token, string.IsNullOrEmpty(symbol) ? "UNKNOWN" : symbol);
                instrumentDict[token] = inst;
            }
            return inst;
        }

        // 1. Populate Candles (Ordered Chronologically!)
        //void LoadCandles(IEnumerable<RawCandleDto> raw, Timeframe tf)
        //{
        //    foreach (var group in raw.GroupBy(x => x.InstrumentToken))
        //    {
        //        var inst = GetOrRegister(group.Key, group.First().TradingSymbol);
        //        var candles = group.OrderBy(c => c.Timestamp)
        //            .DistinctBy(c => c.Timestamp) // <--- THIS LINE
        //            .Select(c => Candle.Create(c.Timestamp, tf, c.Open, c.High, c.Low, c.Close, c.Volume.HasValue ? c.Volume.Value: 0, c.OI.HasValue ? c.OI.Value: 0));
        //        inst.SetCandles(tf, candles);
        //    }
        //}
        void LoadCandles(IEnumerable<RawCandleDto> raw, Timeframe tf)
        {
            // 1. Define the IST Offset
            //var istOffset = TimeSpan.FromHours(5.5);

            foreach (var group in raw.GroupBy(x => x.InstrumentToken))
            {
                var inst = GetOrRegister(group.Key, group.First().TradingSymbol);

                var candles = group
                    .OrderBy(c => c.Timestamp)
                    .Select(c =>
                    {
                        // 2. Force the DateTime to be treated as IST before creating the Candle
                        //var istTime = new DateTimeOffset(c.Timestamp, istOffset);

                        return Candle.Create(
                            c.Timestamp,
                            tf,
                            c.Open, c.High, c.Low, c.Close, c.Volume.HasValue? c.Volume.Value :0, c.OI);
                    })
                    // 3. Group by the EXACT property Candles checks: CloseTime
                    // This guarantees no matter what the Timestamp precision was, 
                    // if Candle.Create results in the same CloseTime, we only take one.
                    .GroupBy(candle => candle.CloseTime)
                    .Select(g => g.First())
                    .ToList();

                inst.SetCandles(tf, candles);
            }
        }
        LoadCandles(raw60Min, new Timeframe(TimeframeUnit.Minute, 60));
        LoadCandles(raw15Min, new Timeframe(TimeframeUnit.Minute, 15));

        // 2. Populate Pivots & Anchors into CriticalStructures
        foreach (var group in pivots.GroupBy(x => x.InstrumentToken))
        {
            if (!instrumentDict.TryGetValue(group.Key, out var inst)) continue;

            foreach (var p in group)
            {
                var tf = p.TimeFrame == "W" ? new Timeframe(TimeframeUnit.Week, 1) : new Timeframe(TimeframeUnit.Day, 1);
                var timeStr = p.TimeFrame; // "D" or "W"

                // --- A. Map Standard Pivots to CriticalLevel ---
                // Format: Id = "D.R1", "W.PP", etc.
                AddLevel(inst, tf, timeStr, "PP", CriticalRole.Pivot, p.TimeStamp, p.PP);
                AddLevel(inst, tf, timeStr, "R1", CriticalRole.Resistance, p.TimeStamp, p.R1);
                AddLevel(inst, tf, timeStr, "R2", CriticalRole.Resistance, p.TimeStamp, p.R2);
                AddLevel(inst, tf, timeStr, "R3", CriticalRole.Resistance, p.TimeStamp, p.R3);
                AddLevel(inst, tf, timeStr, "R4", CriticalRole.Resistance, p.TimeStamp, p.R4);
                AddLevel(inst, tf, timeStr, "R5", CriticalRole.Resistance, p.TimeStamp, p.R5);
                AddLevel(inst, tf, timeStr, "S1", CriticalRole.Support, p.TimeStamp, p.S1);
                AddLevel(inst, tf, timeStr, "S2", CriticalRole.Support, p.TimeStamp, p.S2);
                AddLevel(inst, tf, timeStr, "S3", CriticalRole.Support, p.TimeStamp, p.S3);
                AddLevel(inst, tf, timeStr, "S4", CriticalRole.Support, p.TimeStamp, p.S4);
                AddLevel(inst, tf, timeStr, "S5", CriticalRole.Support, p.TimeStamp, p.S5);

                // --- B. Map Reference Levels (Ref_Resistance / Support) ---
                if (p.Ref_Resistance.HasValue)
                    AddLevel(inst, tf, timeStr, "RefR", CriticalRole.Resistance, p.TimeStamp, p.Ref_Resistance.Value);
                if (p.Ref_Support.HasValue)
                    AddLevel(inst, tf, timeStr, "RefS", CriticalRole.Support, p.TimeStamp, p.Ref_Support.Value);

                // --- C. Map Anchors (Trap Days) ---
                // If it was a trap day (PPR1 or PPS1), the "StartCandle" is the Anchor.
                if (p.Ref_StartCandle_High.HasValue && p.Ref_StartCandle_Low.HasValue)
                {
                    if (p.PPR1)
                    {
                        // Upside trap: it failed to break R1, so the anchor is attached to R1
                        inst.Structures.SetAnchor(new Anchor(
                            StructureId: $"{timeStr}.R1",
                            BreakDirection: BreakDirection.Up,
                            CandleCloseTime: p.TimeStamp,
                            High: p.Ref_StartCandle_High.Value,
                            Low: p.Ref_StartCandle_Low.Value
                        ));
                    }
                    else if (p.PPS1)
                    {
                        // Downside trap: it failed to break S1, attached to S1
                        inst.Structures.SetAnchor(new Anchor(
                            StructureId: $"{timeStr}.S1",
                            BreakDirection: BreakDirection.Down,
                            CandleCloseTime: p.TimeStamp,
                            High: p.Ref_StartCandle_High.Value,
                            Low: p.Ref_StartCandle_Low.Value
                        ));
                    }
                }
                // --- D. Map References (If needed) ---
                // The Reference object captures a full OHLC snapshot. 
                // If the "Ref_StartCandle" is what the rules refer to, map it here.
                if (p.Ref_StartCandle_Open.HasValue)
                {
                    inst.References.Set(new Reference(
                        Id: $"{timeStr}.TrapRef",
                        Timeframe: tf,
                        BarOpenTime: p.TimeStamp, // Approximate, adjust if you have exact open time
                        BarCloseTime: p.TimeStamp,
                        Open: p.Ref_StartCandle_Open.Value,
                        High: p.Ref_StartCandle_High!.Value,
                        Low: p.Ref_StartCandle_Low!.Value,
                        Close: p.Ref_StartCandle_Close!.Value,
                        CapturedAtEvaluationTime: p.TimeStamp
                    ));
                }
            }
        }
        
        return instrumentDict;
    }

    private static void AddLevel(Instruments inst, Timeframe tf, string timeStr, string type, CriticalRole role, DateTimeOffset time, decimal price)
    {
        // Generates Id like "D.R1", "W.PP"
        string id = $"{timeStr}.{type}";
        inst.Structures.Add(new CriticalLevel(id, role, "Traditional", tf, time, price));
    }

    // Inside GetWarmupSnapshotAsync, after inst.SetCandles(tf, candles);

    
    
}

// Updated to match the 29 columns from your SQL query exactly
public record PivotRowDto(
    long InstrumentToken, DateTime TimeStamp, string TimeFrame, // Changed DateTimeOffset to DateTime
    decimal Open, decimal High, decimal Low, decimal Close,
    decimal PP, decimal R1, decimal R2, decimal R3, decimal R4, decimal R5,
    decimal S1, decimal S2, decimal S3, decimal S4, decimal S5,
    bool PPR1, bool PPS1,
    decimal? Ref_Resistance, decimal? Ref_Support,
    decimal? Ref_StartCandle_Open, decimal? Ref_StartCandle_High, decimal? Ref_StartCandle_Low, decimal? Ref_StartCandle_Close,
    decimal? Ref_EndCandle_Open, decimal? Ref_EndCandle_High, decimal? Ref_EndCandle_Low, decimal? Ref_EndCandle_Close,
    bool IsWeekInDev
);
//"[[InstrumentToken, 265], [TimeStamp, 07-08-2026 09:15:00], [TimeFrame, 60        ], [Open, 78516.08], [High, 78757.40]...]"
//signature (Int64 InstrumentToken, System.DateTime TimeStamp, System.String TimeFrame,
//Decimal Open, Decimal High, Decimal Low, Decimal Close,
//System.Int64 Volume, System.Int64 OI, System.String TradingSymbol) 
public record RawCandleDto(
    long InstrumentToken, DateTime Timestamp, string TimeFrame,
    decimal Open, decimal High, decimal Low, decimal Close,
    long? Volume, long? OI, string TradingSymbol
);
