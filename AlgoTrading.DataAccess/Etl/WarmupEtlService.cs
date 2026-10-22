using AlgoTrading.DataAccess.FlatFiles;
using Stage1Scanner.Core.Market;
using System.Diagnostics;

namespace AlgoTrading.DataAccess.Etl;

public class WarmupEtlService
{
    private readonly WarmupEtlRepository _repository;

    public WarmupEtlService(WarmupEtlRepository repository)
    {
        _repository = repository;
    }

    public async Task RunWarmupEtlAsync(string scannerFolder, string outputFolder)
    {
        Console.WriteLine("[ETL] Starting Warmup ETL...");

        // 1. Read JSON Blueprints to find required timeframes and indicators
        var dependencyRegistry = new ScannerDependencyRegistry();
        dependencyRegistry.LoadFromDirectory(scannerFolder);

        var timeframes = dependencyRegistry.GetActiveTimeframes().ToList();
        if (!timeframes.Any())
        {
            Console.WriteLine("[ETL] No scanner JSONs found. Exiting.");
            return;
        }

        var equitySnapshot = new SnapshotFile { SnapshotDate = DateTime.Today, AssetClass = "Equities" };
        var foSnapshot = new SnapshotFile { SnapshotDate = DateTime.Today, AssetClass = "FO" };

        // 2. Process each Timeframe independently
        foreach (var tfStr in timeframes)
        {
            Console.WriteLine($"[ETL] Fetching 1-year history for Timeframe: {tfStr}...");

            // Fetch raw data from SQL
            var rawData = await _repository.FetchOneYearHistoryAsync(tfStr);
            var coreTimeframe = ParseTimeframe(tfStr);

            // Group data by Instrument to build the Instruments object
            var groups = rawData.GroupBy(x => x.InstrumentToken);

            foreach (var group in groups)
            {
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
                        new DateTimeOffset(c.TimeStamp, TimeSpan.FromHours(5.5)), // Force IST offset
                        coreTimeframe,
                        c.Open, c.High, c.Low, c.Close,
                        c.Volume ?? 0, c.OI))
                    .ToList();

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
                        // Key format: 265_60_SUPERTREND_10_Value
                        string key = $"{token}_{tfStr}_{req.IndicatorName}_{req.Period}_{output.Key}";
                        targetSnapshot.IndicatorSeries[key] = output.Value;
                    }
                }
            }
        }

        // 4. Save to Disk
        if (!Directory.Exists(outputFolder))
        {
            Directory.CreateDirectory(outputFolder);
        }   

        string equityPath = Path.Combine(outputFolder, $"Warmup_Equities_{DateTime.Today:yyyyMMdd}.json");
        string foPath = Path.Combine(outputFolder, $"Warmup_FO_{DateTime.Today:yyyyMMdd}.json");

        Console.WriteLine("[ETL] Saving Equities Snapshot...");
        await FlatFileManager.SaveSnapshotAsync(equityPath, equitySnapshot);

        Console.WriteLine("[ETL] Saving F&O Snapshot...");
        await FlatFileManager.SaveSnapshotAsync(foPath, foSnapshot);

        Console.WriteLine("[ETL] Warmup Complete!");
    }

    private Timeframe ParseTimeframe(string tf)
    {
        return tf.ToUpper() switch
        {
            "D" => new Timeframe(TimeframeUnit.Day, 1),
            "W" => new Timeframe(TimeframeUnit.Week, 1),
            _ => new Timeframe(TimeframeUnit.Minute, int.Parse(tf))
        };
    }

    /// <summary>
    /// Returns a dictionary of outputs. 
    /// Keys: "Value", "Direction", "Smooth", "Divergence" (depending on the indicator)
    /// </summary>
    private Dictionary<string, List<decimal?>> ComputeIndicator(Instruments inst, Timeframe tf, RequiredIndicator req)
    {
        var results = new Dictionary<string, List<decimal?>>();

        if (!inst.TryGetCandles(tf, out var candles)) return results;

        // =====================================================================
        // TODO: REPLACE THESE PLACEHOLDERS WITH YOUR STAGE1SCANNER.CORE MATH
        // =====================================================================
        switch (req.IndicatorName)
        {
            case "EMA":
                // Example: var ema = CoreMath.CalculateEma(candles, req.Period);
                // results["Value"] = ema;
                results["Value"] = new List<decimal?>(); // Placeholder
                break;

            case "RSI":
                // Example: var rsi = CoreMath.CalculateRsi(candles, req.Period);
                results["Value"] = new List<decimal?>(); // Placeholder
                break;

            case "SUPERTREND":
                // Example: var st = CoreMath.CalculateSuperTrend(candles, req.Period);
                // results["Value"] = st.ValueLine;
                // results["Direction"] = st.DirectionLine;
                results["Value"] = new List<decimal?>();     // Placeholder
                results["Direction"] = new List<decimal?>(); // Placeholder
                break;

            default:
                Console.WriteLine($"[ETL Warning] Unknown indicator: {req.IndicatorName}");
                break;
        }

        return results;
    }
}