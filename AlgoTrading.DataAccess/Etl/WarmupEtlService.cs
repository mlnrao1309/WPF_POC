using AlgoTrading.DataAccess.FlatFiles;
using Stage1Scanner.Core.Market;
using AlgoTrading.Models;
using AlgoTrading.DataAccess.Repositories;
using Stage1Scanner.Core.Indicators;
using Candle = Stage1Scanner.Core.Market.Candle;

namespace AlgoTrading.DataAccess.Etl;

public class WarmupEtlService
{
    private readonly WarmupEtlRepository _repository;
    private readonly IWarmupRepository? _warmupRepository;

    public WarmupEtlService(WarmupEtlRepository repository, IWarmupRepository? warmupRepository = null)
    {
        _repository = repository;
        _warmupRepository = warmupRepository;
    }

    public async Task RunWarmupEtlAsync(string scannerFolder, string outputFolder, CancellationToken cancellationToken = default, ExchangeCalendarSnapshot? calendar = null)
    {
        Console.WriteLine("[ETL] Starting Warmup ETL...");

        // 1. Read JSON Blueprints to find required timeframes and indicators
        var dependencyRegistry = new ScannerDependencyRegistry();
        dependencyRegistry.LoadFromDirectory(scannerFolder);

        var timeframes = dependencyRegistry.GetActiveTimeframes().ToList();
        if (!timeframes.Any())
        {
            throw new InvalidOperationException("No scanner timeframes were found; warmup cannot start.");
        }

        DateTime marketDate = MarketTimestamp.ToDatabase(DateTimeOffset.UtcNow).Date;
        Guid generation = Guid.NewGuid();
        var sessionDays = calendar?.Days.ToDictionary(day => day.Date);
        DateTimeOffset asOf = DateTimeOffset.UtcNow;
        var requirements = timeframes.SelectMany(dependencyRegistry.GetIndicatorsForTimeframe).ToList();
        var equitySnapshot = new SnapshotFile { FormatVersion = 2, SnapshotDate = marketDate, AssetClass = "Equities",
            Generation = generation, RequiredTimeframes = timeframes, DependencyFingerprint = dependencyRegistry.Fingerprint, IndicatorRequirements = requirements };
        var foSnapshot = new SnapshotFile { FormatVersion = 2, SnapshotDate = marketDate, AssetClass = "FO",
            Generation = generation, RequiredTimeframes = timeframes, DependencyFingerprint = dependencyRegistry.Fingerprint, IndicatorRequirements = requirements };

        // 2. Process each Timeframe independently
        foreach (var tfStr in timeframes)
        {
            Console.WriteLine($"[ETL] Fetching 1-year history for Timeframe: {tfStr}...");

            // Fetch raw data from SQL
            cancellationToken.ThrowIfCancellationRequested();
            var rawData = await _repository.FetchOneYearHistoryAsync(tfStr, cancellationToken);
            var coreTimeframe = ParseTimeframe(tfStr);

            // Group data by Instrument to build the Instruments object
            var groups = rawData.GroupBy(x => x.InstrumentToken);

            foreach (var group in groups)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var token = group.Key;
                var isFno = group.First().IsFno;
                var symbol = group.First().Symbol;

                // Determine target snapshot (Equity or FO)
                var targetSnapshot = isFno ? foSnapshot : equitySnapshot;

                // Create or get the Core Instruments object
                if (!targetSnapshot.Instruments.TryGetValue(token, out var instrument))
                {
                    instrument = new Instruments(token, symbol);
                    targetSnapshot.Instruments[token] = instrument;
                }

                // Convert SQL DTOs to Core Candles
                var candles = group
                    .OrderBy(c => c.TimeStamp)
                    .Select(c => Candle.Create(
                        MarketTimestamp.FromDatabase(c.TimeStamp),
                        coreTimeframe,
                        c.Open, c.High, c.Low, c.Close,
                        c.Volume ?? 0, c.OI))
                    .Select(c =>
                    {
                        // Daily and final intraday candles close at the exchange session boundary.
                        // Older dates outside this calendar revision retain their nominal interval.
                        if (sessionDays != null && coreTimeframe.Unit is TimeframeUnit.Minute or TimeframeUnit.Day
                            && sessionDays.TryGetValue(DateOnly.FromDateTime(c.OpenTime.Date), out var session))
                        {
                            if (!session.ClosesAt.HasValue)
                                throw new InvalidDataException($"History contains a candle on closed trading date {c.OpenTime:d}.");
                            DateTimeOffset close = MarketTimestamp.FromDatabase(session.Date.ToDateTime(session.ClosesAt.Value));
                            if (coreTimeframe.Unit == TimeframeUnit.Day || c.CloseTime > close)
                                c = c with { CloseTime = close };
                        }
                        return c;
                    })
                    .Where(c => c.CloseTime <= asOf)
                    // Match historical ingestion: the last candle wins for each adjusted close time.
                    .GroupBy(c => c.CloseTime)
                    .Select(candlesAtClose => candlesAtClose.Last())
                    .ToList();
                if (candles.Count == 0)
                    throw new InvalidDataException($"No completed candles for {symbol}/{tfStr}.");

                // Set Candles
                instrument.SetCandles(coreTimeframe, candles);

                // 3. Compute required indicators for this timeframe
                var requiredIndicators = dependencyRegistry.GetIndicatorsForTimeframe(tfStr);
                foreach (var req in requiredIndicators)
                {
                    // Returns a Dictionary: { "Value": [...], "Direction": [...] }
                    var outputs = ComputeIndicator(instrument, coreTimeframe, req);

                    // Save each output series into the Snapshot
                    foreach (var output in outputs)
                    {
                        // Include input and factor so distinct scanner requirements cannot collide.
                        string key = $"{token}_{tfStr}_{req.UniqueKey}_{output.Key}";
                        targetSnapshot.IndicatorSeries[key] = output.Value;
                    }
                }
            }
        }

        if (equitySnapshot.Instruments.Count + foSnapshot.Instruments.Count == 0)
            throw new InvalidDataException("Warmup returned no instruments.");

        if (_warmupRepository != null)
        {
            // Reuse the existing pivot mapping rather than creating a second SQL mapping.
            var structures = await _warmupRepository.GetWarmupSnapshotAsync(52);
            foreach (var instrument in equitySnapshot.Instruments.Values.Concat(foSnapshot.Instruments.Values))
            {
                if (!structures.TryGetValue(instrument.InstrumentToken, out var source)) continue;
                foreach (var level in source.Structures.Levels) instrument.Structures.Add(level);
                foreach (var anchor in source.Structures.Anchors) instrument.Structures.SetAnchor(anchor);
                foreach (var reference in source.References.All) instrument.References.Set(reference);
            }
        }

        // Preserve the least recent source date represented by the completed series, not the machine date.
        DateTime sourceDate = equitySnapshot.Instruments.Values.Concat(foSnapshot.Instruments.Values)
            .SelectMany(inst => inst.AvailableTimeframes.Select(tf =>
                inst.GetCandles(tf)[^1].OpenTime.ToOffset(TimeSpan.FromMinutes(330)).Date)).Min();
        equitySnapshot.SnapshotDate = foSnapshot.SnapshotDate = sourceDate;

        // Both asset files carry the same generation; an incomplete pair is never loaded.
        // 4. Save to Disk
        if (!Directory.Exists(outputFolder))
        {
            Directory.CreateDirectory(outputFolder);
        }   

        string equityPath = Path.Combine(outputFolder, $"Warmup_Equities_{marketDate:yyyyMMdd}_{generation:N}.json");
        string foPath = Path.Combine(outputFolder, $"Warmup_FO_{marketDate:yyyyMMdd}_{generation:N}.json");

        Console.WriteLine("[ETL] Saving Equities Snapshot...");
        await FlatFileManager.SaveSnapshotAsync(equityPath, equitySnapshot, cancellationToken);

        Console.WriteLine("[ETL] Saving F&O Snapshot...");
        await FlatFileManager.SaveSnapshotAsync(foPath, foSnapshot, cancellationToken);

        Console.WriteLine("[ETL] Warmup Complete!");
    }

    internal static Timeframe ParseTimeframe(string tf)
    {
        string value = tf.Trim().ToUpperInvariant();
        if (value is "1D" or "1W" or "1M") value = value[1..];
        var definition = new TimeframeNormalizer().Normalize(value);
        return definition.Kind switch
        {
            TimeframeKind.Daily => Timeframe.Day,
            TimeframeKind.Weekly => Timeframe.Week,
            TimeframeKind.Monthly => Timeframe.Month,
            _ => Timeframe.Minutes(checked((int)definition.Duration!.Value.TotalMinutes))
        };
    }

    /// <summary>
    /// Returns a dictionary of outputs. 
    /// Keys: "Value" and, for SuperTrend, "Direction".
    /// </summary>
    public static Dictionary<string, List<decimal?>> ComputeIndicator(Instruments inst, Timeframe tf, RequiredIndicator req)
    {
        var candles = inst.GetCandles(tf);
        if (req.Period <= 0 || req.Factor <= 0)
            throw new ArgumentOutOfRangeException(nameof(req), "Indicator parameters must be positive.");
        if (!Enum.TryParse<SourceField>(req.InputField, true, out var source) || !Enum.IsDefined(source))
            throw new ArgumentException("Unsupported indicator source.", nameof(req));
        // Warmup and live refresh use the same tested calculations and warmup nulls.
        var results = new Dictionary<string, List<decimal?>>();
        switch (req.IndicatorName)
        {
            case "EMA":
                results["Value"] = candles.EMA(req.Period, source).Values.ToList();
                break;
            case "RSI":
                results["Value"] = candles.RSI(req.Period, source: source).Value.Values.ToList();
                break;
            case "SUPERTREND":
                if (source != SourceField.Close)
                    throw new ArgumentException("SuperTrend uses OHLC; a custom source is not supported.", nameof(req));
                var trend = candles.SuperTrend(req.Period, req.Factor);
                results["Value"] = trend.Value.Values.ToList();
                results["Direction"] = trend.Direction.Select(value => value.HasValue ? (decimal?)(int)value.Value : null).ToList();
                break;
            default:
                throw new NotSupportedException($"Unsupported warmup indicator '{req.IndicatorName}'.");
        }
        return results;
    }
}
