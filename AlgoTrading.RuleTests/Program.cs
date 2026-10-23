using AlgoTrading.DataAccess.Etl;
using AlgoTrading.DataAccess.FlatFiles;
using AlgoTrading.DataAccess.Repositories;
using AlgoTrading.DataAccess.Runtime;
using Stage1Scanner.Core.Market;

internal static class Program
{
    private static async Task<int> Main(string[] arguments)
    {
        try
        {
            await RunAsync(arguments);
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.ToString());
            return 1;
        }
    }

    private static async Task RunAsync(string[] arguments)
    {
        if (arguments.Length == 1 && arguments[0] == "--screener-indicators")
        {
            new ScreenerIndicatorTests().Run();
            return;
        }
        if (arguments.Length == 1 && arguments[0] == "--dataaccess")
        {
            await VerifyDataAccessAsync();
            return;
        }
        if (arguments.Length == 2 && string.Equals(arguments[0], "--screener-sql", StringComparison.Ordinal))
        {
            await new ScreenerSqlMeasurement().RunAsync(arguments[1]);
            return;
        }

        if (arguments.Length == 1 && string.Equals(arguments[0], "--screener", StringComparison.Ordinal))
        {
            new ScreenerHistoryRequirementTests().Run();
            return;
        }

        if (arguments.Length == 1 && string.Equals(arguments[0], "--screener-plan", StringComparison.Ordinal))
        {
            await new ScreenerExecutionPlanTests().RunAsync();
            return;
        }

        if (await DatabaseTestCommand.TryRunAsync(arguments))
        {
            return;
        }

        await VerifyDataAccessAsync();
        new ScreenerIndicatorTests().Run();
        RuleRegressionTests tests = new RuleRegressionTests();
        tests.Run();
        CandleTimeTests candleTimeTests = new CandleTimeTests();
        candleTimeTests.Run();
        TradingCalendarTests calendarTests = new TradingCalendarTests();
        calendarTests.Run();
        CalendarImportTests importTests = new CalendarImportTests();
        await importTests.RunAsync();
        TimeframeNormalizationTests timeframeTests = new TimeframeNormalizationTests();
        timeframeTests.Run();
        CandleAggregationTests aggregationTests = new CandleAggregationTests();
        aggregationTests.Run();
        CalendarCandleAggregationTests calendarAggregationTests = new CalendarCandleAggregationTests();
        calendarAggregationTests.Run();
        StrategyDependencyTests dependencyTests = new StrategyDependencyTests();
        dependencyTests.Run();
        IndicatorCacheTests cacheTests = new IndicatorCacheTests();
        cacheTests.Run();
        BasicIndicatorCalculatorTests indicatorTests = new BasicIndicatorCalculatorTests();
        indicatorTests.Run();
        IndicatorRecoveryTests recoveryTests = new IndicatorRecoveryTests();
        recoveryTests.Run();
        IntegratedIndicatorTests integratedTests = new IntegratedIndicatorTests();
        integratedTests.Run();
        DailyPivotTests dailyPivotTests = new DailyPivotTests();
        dailyPivotTests.Run();
        HistoricalIngestionTests historicalIngestionTests = new HistoricalIngestionTests();
        await historicalIngestionTests.RunAsync();
        PlatformConfigurationTests platformConfigurationTests = new PlatformConfigurationTests();
        await platformConfigurationTests.RunAsync();
        ClosedCandleProcessingCoordinatorTests closedCandleTests = new ClosedCandleProcessingCoordinatorTests();
        closedCandleTests.Run();
        BoundedMarketDataPipelineTests pipelineTests = new BoundedMarketDataPipelineTests();
        await pipelineTests.RunAsync();
    }
    private static async Task VerifyDataAccessAsync()
    {
        string folder = Path.Combine(Path.GetTempPath(), "AlgoTrading-DataAccess-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var tf = Timeframe.Minutes(15);
            var start = new DateTimeOffset(2026, 10, 20, 9, 15, 0, TimeSpan.FromMinutes(330));
            var instrument = new Instruments(101, "TEST");
            var candles = Enumerable.Range(0, 6).Select(i => Candle.Create(start.AddMinutes(i * 15), tf,
                100 + i, 110 + i, 90 + i, 105 + i, 1000)).ToArray();
            instrument.SetCandles(tf, candles);
            var corrected = candles[1] with { Close = 104 };
            instrument.AppendLiveCandle(tf, corrected);
            instrument.AppendLiveCandle(tf, corrected);
            if (instrument.GetCandles(tf).Count != 6 || instrument.GetCandles(tf)[1].Close != 104)
                throw new Exception("An older correction must replace a candle without duplicating its identity.");
            instrument.AppendLiveCandle(tf, Candle.Create(start.AddMinutes(-15), tf, 100, 110, 90, 105, 1000));
            if (instrument.GetCandles(tf).Count != 7 || instrument.GetCandles(tf)[0].OpenTime != start.AddMinutes(-15))
                throw new Exception("A genuinely new out-of-order candle must be inserted in order.");

            string scannerPath = Path.Combine(folder, "scanner.json");
            await File.WriteAllTextAsync(scannerPath,
                """{"timeframe":"15minute","indicators":[{"indicator":"EMA","inputs":["Close"],"parameters":["3"]},{"indicator":"EMA","inputs":["Open"],"parameters":["3"]},{"indicator":"SUPERTREND","inputs":["High","Low","Close"],"parameters":["3","2"]}]}""");
            var dependencies = new ScannerDependencyRegistry();
            dependencies.LoadFromDirectory(folder);
            var requirements = dependencies.GetIndicatorsForTimeframe("15").ToList();
            if (requirements.Count != 3 || requirements.Select(x => x.UniqueKey).Distinct().Count() != 3)
                throw new Exception("Indicator identity must preserve input and factor.");
            var requirement = requirements[0];
            var calculated = WarmupEtlService.ComputeIndicator(instrument, tf, requirement);
            if (calculated["Value"].Count != 7 || calculated["Value"][0] != null || calculated["Value"][2] == null)
                throw new Exception("EMA output must align with candles and preserve warmup nulls.");
            var rsi = WarmupEtlService.ComputeIndicator(instrument, tf, new RequiredIndicator
                { IndicatorName = "RSI", InputField = "Close", Period = 3 });
            if (rsi["Value"].Count != 7 || rsi["Value"][0] != null || rsi["Value"][3] == null)
                throw new Exception("RSI calculation is incomplete.");
            var trend = WarmupEtlService.ComputeIndicator(instrument, tf, requirements[2]);
            if (trend["Value"].Count != 7 || trend["Direction"].Count != 7)
                throw new Exception("SuperTrend outputs must align with candles.");

            instrument.Structures.Add(new CriticalLevel("D.R1", CriticalRole.Resistance, "Traditional", Timeframe.Day, start, 110));
            instrument.Structures.SetAnchor(new Anchor("D.R1", BreakDirection.Up, start, 110, 90));
            instrument.Structures.Add(new CriticalZone("zone", CriticalRole.Support, "Test", tf, start, 90, 95));
            instrument.References.Set(new Reference("ref", tf, start, start.AddMinutes(15), 100, 110, 90, 105, start.AddMinutes(15)));
            Guid generation = Guid.NewGuid();
            var equity = new SnapshotFile { FormatVersion = 2, Generation = generation,
                SnapshotDate = start.Date, AssetClass = "Equities", RequiredTimeframes = ["15"],
                DependencyFingerprint = dependencies.Fingerprint, IndicatorRequirements = requirements,
                Instruments = new() { [101] = instrument } };
            foreach (var req in requirements)
                foreach (var output in WarmupEtlService.ComputeIndicator(instrument, tf, req))
                    equity.IndicatorSeries[$"101_15_{req.UniqueKey}_{output.Key}"] = output.Value;
            string equityPath = Path.Combine(folder, $"Warmup_Equities_20261020_{generation:N}.json");
            string foPath = Path.Combine(folder, $"Warmup_FO_20261020_{generation:N}.json");
            var fo = new SnapshotFile { FormatVersion = 2, Generation = generation, SnapshotDate = start.Date,
                AssetClass = "FO", RequiredTimeframes = ["15"], DependencyFingerprint = dependencies.Fingerprint,
                IndicatorRequirements = requirements };
            await FlatFileManager.SaveSnapshotAsync(equityPath, equity);
            var loader = new SnapshotLoader(folder);
            if ((await loader.LoadLatestSnapshotAsync()).Instruments.Count != 0)
                throw new Exception("A partial snapshot pair must be rejected.");
            await FlatFileManager.SaveSnapshotAsync(foPath, fo);
            var loaded = await loader.LoadLatestSnapshotAsync(start.Date, dependencies.Fingerprint);
            if (loaded.Instruments.Count != 1 || loaded.SnapshotDate != start.Date
                || !loaded.Instruments[101].Structures.TryGetAnchor("D.R1", out _)
                || !loaded.Instruments[101].Structures.TryGetZone("zone", out _)
                || !loaded.Instruments[101].References.All.Any())
                throw new Exception("Snapshot round trip must preserve its source date and rule state.");
            if ((await loader.LoadLatestSnapshotAsync(start.Date.AddDays(1))).Instruments.Count != 0
                || (await loader.LoadLatestSnapshotAsync(start.Date, "changed")).Instruments.Count != 0)
                throw new Exception("Stale history or changed scanner dependencies must invalidate a snapshot.");
            fo.Generation = Guid.NewGuid();
            await FlatFileManager.SaveSnapshotAsync(foPath, fo);
            if ((await loader.LoadLatestSnapshotAsync()).Instruments.Count != 0)
                throw new Exception("Mixed snapshot generations must be rejected.");
            fo.Generation = generation;
            await FlatFileManager.SaveSnapshotAsync(foPath, fo);

            var registry = new InstrumentRegistry();
            registry.Initialize(loaded);
            var values = registry.GetIndicatorData(101, "15minute", "EMA", 3)!;
            decimal? oldTail = values["Value"][^1];
            values["Value"].Clear();
            if (registry.GetIndicatorData(101, "15", "EMA", 3)!["Value"].Count != 7)
                throw new Exception("Callers must not mutate registry output.");
            var current = loaded.Instruments[101];
            current.AppendLiveCandle(tf, current.GetCandles(tf)[^1] with { Close = 90 });
            if (registry.GetIndicatorData(101, "15", "EMA", 3)!["Value"][^1] == oldTail)
                throw new Exception("A candle correction must invalidate indicator calculations.");
            await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
            {
                for (int i = 0; i < 25; i++)
                {
                    current.AppendLiveCandle(tf, corrected);
                    _ = registry.GetIndicatorData(101, "15", "EMA", 3)!["Value"].Count;
                    _ = current.AvailableTimeframes.Count();
                }
            })));
            registry.Initialize(new RuntimeSnapshot());
            if (registry.GetAll().Count != 0 || registry.GetIndicatorData(101, "15", "EMA", 3) != null)
                throw new Exception("Registry reload retained obsolete state.");

            await File.WriteAllTextAsync(scannerPath, """{"timeframe":"1D","indicators":[]}""");
            dependencies.LoadFromDirectory(folder);
            if (!dependencies.GetActiveTimeframes().SequenceEqual(new[] { "D" }))
                throw new Exception("Price-only scanners must still contribute a normalized timeframe.");
            await File.WriteAllTextAsync(scannerPath, """{"timeframe":"15","indicators":[{"indicator":"EMA","parameters":["3","99"]}]}""");
            bool invalidRejected = false;
            try { dependencies.LoadFromDirectory(folder); }
            catch (InvalidDataException) { invalidRejected = true; }
            if (!invalidRejected) throw new Exception("Unsupported extra indicator parameters must not be discarded.");
            await File.WriteAllTextAsync(foPath, "{broken");
            if ((await loader.LoadLatestSnapshotAsync()).Instruments.Count != 0)
                throw new Exception("Corrupt snapshot JSON must not produce partial runtime state.");
            if (Directory.GetFiles(folder, "*.tmp").Length != 0)
                throw new Exception("Snapshot writes left temporary files behind.");
            Console.WriteLine("Passed data-access candle correction, indicator, concurrency, dependency and snapshot regression checks.");
        }
        finally
        {
            // The test owns this unique directory and all files it creates there.
            Directory.Delete(folder, recursive: true);
        }
    }

}

