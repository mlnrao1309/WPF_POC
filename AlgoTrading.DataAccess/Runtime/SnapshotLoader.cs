using AlgoTrading.DataAccess.FlatFiles;

namespace AlgoTrading.DataAccess.Runtime;

public class SnapshotLoader
{
    private readonly string _resourcesFolder;

    public SnapshotLoader(string resourcesFolder)
    {
        _resourcesFolder = resourcesFolder;
    }

    public async Task<RuntimeSnapshot> LoadLatestSnapshotAsync(DateTime? expectedSessionDate = null,
        string? expectedFingerprint = null, CancellationToken cancellationToken = default)
    {
        var snapshot = new RuntimeSnapshot();
        var equityFile = GetLatestFile("Warmup_Equities_*.json");
        if (equityFile == null) return snapshot;
        // Derive the companion filename, rather than mixing independently selected asset files.
        string foFile = Path.Combine(_resourcesFolder, Path.GetFileName(equityFile).Replace("Warmup_Equities_", "Warmup_FO_"));
        var equity = await FlatFileManager.LoadSnapshotAsync(equityFile, cancellationToken);
        var fo = await FlatFileManager.LoadSnapshotAsync(foFile, cancellationToken);
        if (equity == null || fo == null || equity.FormatVersion != 2 || fo.FormatVersion != 2
            || equity.Generation == Guid.Empty || equity.Generation != fo.Generation
            || equity.SnapshotDate != fo.SnapshotDate || equity.AssetClass != "Equities" || fo.AssetClass != "FO"
            || equity.IndicatorRequirements == null || fo.IndicatorRequirements == null
            || equity.RequiredTimeframes == null || fo.RequiredTimeframes == null
            || equity.RequiredTimeframes.Count == 0
            || equity.IndicatorRequirements.Any(x => x == null) || fo.IndicatorRequirements.Any(x => x == null)
            || !equity.RequiredTimeframes.SequenceEqual(fo.RequiredTimeframes)
            || !equity.IndicatorRequirements.Select(x => x.Timeframe + "_" + x.UniqueKey)
                .SequenceEqual(fo.IndicatorRequirements.Select(x => x.Timeframe + "_" + x.UniqueKey))
            || equity.DependencyFingerprint != fo.DependencyFingerprint
            || (expectedFingerprint != null && equity.DependencyFingerprint != expectedFingerprint))
        {
            System.Diagnostics.Trace.TraceWarning("Warmup snapshot pair is missing, incompatible or from different generations.");
            return snapshot;
        }

        foreach (var asset in new[] { equity, fo })
        {
            if (asset.Instruments == null || asset.IndicatorSeries == null || asset.IndicatorRequirements == null)
                return new RuntimeSnapshot();
            foreach (var entry in asset.Instruments)
            {
                var instrument = entry.Value;
                if (instrument == null || entry.Key != instrument.InstrumentToken
                    || !instrument.AvailableTimeframes.Any() || snapshot.Instruments.ContainsKey(entry.Key))
                    return new RuntimeSnapshot();
                foreach (string required in asset.RequiredTimeframes)
                    if (!instrument.TryGetCandles(AlgoTrading.DataAccess.Etl.WarmupEtlService.ParseTimeframe(required), out _))
                        return new RuntimeSnapshot();
                foreach (var tf in instrument.AvailableTimeframes)
                {
                    var candles = instrument.GetCandles(tf);
                    if (candles.Count == 0 || (expectedSessionDate.HasValue
                        && tf.Unit is Stage1Scanner.Core.Market.TimeframeUnit.Minute or Stage1Scanner.Core.Market.TimeframeUnit.Day
                        && candles[^1].OpenTime.ToOffset(TimeSpan.FromMinutes(330)).Date < expectedSessionDate.Value.Date))
                    {
                        System.Diagnostics.Trace.TraceWarning($"Warmup data is empty or stale for {instrument.Symbol}/{tf}.");
                        return new RuntimeSnapshot();
                    }
                }
                foreach (var requirement in asset.IndicatorRequirements)
                {
                    var tf = AlgoTrading.DataAccess.Etl.WarmupEtlService.ParseTimeframe(requirement.Timeframe);
                    if (!instrument.TryGetCandles(tf, out var candles)) return new RuntimeSnapshot();
                    string prefix = $"{entry.Key}_{requirement.Timeframe}_{requirement.UniqueKey}_";
                    string[] outputs = requirement.IndicatorName == "SUPERTREND" ? ["Value", "Direction"] : ["Value"];
                    foreach (string output in outputs)
                        if (!asset.IndicatorSeries.TryGetValue(prefix + output, out var values)
                            || values == null || values.Count != candles.Count)
                            return new RuntimeSnapshot();
                }
                snapshot.Instruments.Add(entry.Key, instrument);
            }
            foreach (var entry in asset.IndicatorSeries)
            {
                if (!snapshot.IndicatorSeries.TryAdd(entry.Key, entry.Value)) return new RuntimeSnapshot();
            }
        }
        if (snapshot.Instruments.Count == 0) return new RuntimeSnapshot();
        snapshot.SnapshotDate = equity.SnapshotDate;
        snapshot.Generation = equity.Generation;
        System.Diagnostics.Trace.WriteLine($"[Loader] Loaded {snapshot.Instruments.Count} instruments from generation {equity.Generation}.");
        return snapshot;
    }

    /// <summary>Saves a matching pair from the current registry without querying SQL.</summary>
    public async Task<Guid> SaveLatestSnapshotAsync(AlgoTrading.DataAccess.Repositories.IInstrumentRegistry registry,
        CancellationToken cancellationToken = default)
    {
        string equityPath = GetLatestFile("Warmup_Equities_*.json")
            ?? throw new InvalidOperationException("No snapshot metadata is available for saving.");
        string foPath = Path.Combine(_resourcesFolder, Path.GetFileName(equityPath).Replace("Warmup_Equities_", "Warmup_FO_"));
        var equity = await FlatFileManager.LoadSnapshotAsync(equityPath, cancellationToken);
        var fo = await FlatFileManager.LoadSnapshotAsync(foPath, cancellationToken);
        if (equity == null || fo == null || equity.Generation != fo.Generation
            || equity.FormatVersion != 2 || fo.FormatVersion != 2)
            throw new InvalidOperationException("Snapshot metadata is incomplete; the previous files were retained.");

        Guid generation = Guid.NewGuid();
        DateTime? oldestDate = null;
        foreach (var asset in new[] { equity, fo })
        {
            asset.IndicatorSeries.Clear();
            foreach (long token in asset.Instruments.Keys.ToArray())
            {
                var current = registry.GetByToken(token)
                    ?? throw new InvalidOperationException($"Instrument {token} is missing while saving.");
                var saved = new Stage1Scanner.Core.Market.Instruments(token, current.Symbol);
                lock (current)
                {
                    // Candle collections are immutable; capture them before serializing in the background.
                    foreach (var timeframe in current.AvailableTimeframes)
                        saved.SetCandles(current.GetCandles(timeframe));
                    foreach (var level in current.Structures.Levels) saved.Structures.Add(level);
                    foreach (var anchor in current.Structures.Anchors) saved.Structures.SetAnchor(anchor);
                    foreach (var zone in current.Structures.Zones) saved.Structures.Add(zone);
                    foreach (var reference in current.References.All) saved.References.Set(reference);
                }
                foreach (var timeframe in saved.AvailableTimeframes)
                {
                    var candles = saved.GetCandles(timeframe);
                    if (candles.Count == 0) throw new InvalidDataException($"Cannot save empty history for {saved.Symbol}/{timeframe}.");
                    DateTime date = candles[^1].OpenTime.ToOffset(TimeSpan.FromMinutes(330)).Date;
                    if (!oldestDate.HasValue || date < oldestDate.Value) oldestDate = date;
                }
                foreach (var requirement in asset.IndicatorRequirements)
                {
                    var timeframe = AlgoTrading.DataAccess.Etl.WarmupEtlService.ParseTimeframe(requirement.Timeframe);
                    var outputs = AlgoTrading.DataAccess.Etl.WarmupEtlService.ComputeIndicator(saved, timeframe, requirement);
                    foreach (var output in outputs)
                        asset.IndicatorSeries[$"{token}_{requirement.Timeframe}_{requirement.UniqueKey}_{output.Key}"] = output.Value;
                }
                asset.Instruments[token] = saved;
            }
            asset.Generation = generation;
        }
        if (!oldestDate.HasValue) throw new InvalidDataException("There is no history to save.");
        equity.SnapshotDate = fo.SnapshotDate = oldestDate.Value;
        DateTime marketDate = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromMinutes(330)).Date;
        string suffix = $"{marketDate:yyyyMMdd}_{generation:N}.json";
        // Publish the companion first: the loader discovers a pair through its equity filename.
        await FlatFileManager.SaveSnapshotAsync(Path.Combine(_resourcesFolder, "Warmup_FO_" + suffix), fo, cancellationToken);
        await FlatFileManager.SaveSnapshotAsync(Path.Combine(_resourcesFolder, "Warmup_Equities_" + suffix), equity, cancellationToken);
        return generation;
    }

    private string? GetLatestFile(string searchPattern)
    {
        if (!Directory.Exists(_resourcesFolder)) return null;
        
        var dir = new DirectoryInfo(_resourcesFolder);
        var files = dir.GetFiles(searchPattern).OrderByDescending(f => f.LastWriteTime).ToList();
        
        return files.FirstOrDefault()?.FullName;
    }
}