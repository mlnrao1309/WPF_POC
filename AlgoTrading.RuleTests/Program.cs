using AlgoTrading.DataAccess.Etl;
using AlgoTrading.DataAccess.FlatFiles;
using AlgoTrading.DataAccess.Repositories;
using AlgoTrading.DataAccess.Runtime;
using Stage1Scanner.Core.Market;
using Stage1Scanner.Core.Indicators;
using Stage1Scanner.Core.Pivots;
using Stage1Scanner.Core.Rules;
using Stage1Scanner.Core.ScannerModel;

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
        if (arguments.Length == 1 && arguments[0] == "--stage1-volume-pivots")
        {
            new Stage1VolumeAndPivotTests().Run();
            return;
        }
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
        new Stage1VolumeAndPivotTests().Run();
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

internal sealed class Stage1VolumeAndPivotTests
{
    private int assertions;
    private static readonly DateTimeOffset Start = new(2026, 10, 19, 9, 15, 0, TimeSpan.FromMinutes(330));

    public void Run()
    {
        var tf = Timeframe.Minutes(15);
        var adlBars = new Candles(tf, new[]
        {
            Bar(0, tf, 12, 8, 11, 100), Bar(1, tf, 14, 10, 11, 200),
            Bar(2, tf, 10, 10, 10, 999), Bar(3, tf, 20, 10, 20, 0)
        });
        Equal(adlBars.ADL().Values, new decimal?[] { 50, -50, -50, -50 }, "ADL money-flow volume and zero range/volume");
        Assert(ReferenceEquals(adlBars.ADL(), adlBars.ADL()), "ADL cache reuse");

        var mfiBars = Prices(10, 12, 11, 13, 9);
        Equal(mfiBars.MFI(2).Values, new decimal?[]
        {
            null, null, 100m - 100m / (1m + 24m / 33m),
            100m - 100m / (1m + 52m / 33m), 100m - 100m / (1m + 52m / 45m)
        }, "MFI rolling window");
        Equal(Prices(10, 11, 12).MFI(2).Values, new decimal?[] { null, null, 100 }, "MFI all positive");
        Equal(Prices(12, 11, 10).MFI(2).Values, new decimal?[] { null, null, 0 }, "MFI all negative");
        Equal(Prices(10, 10, 10).MFI(2).Values, new decimal?[] { null, null, null }, "MFI unchanged typical price");
        var typicalPrices = new Candles(tf, new[]
        {
            Bar(0, tf, 12, 8, 10, 100), Bar(1, tf, 24, 8, 10, 100), Bar(2, tf, 12, 6, 9, 100)
        });
        Equal(typicalPrices.MFI(1).Values, new decimal?[] { null, 100, 0 }, "MFI uses typical price rather than close changes");
        Equal(new Candles(tf, mfiBars.Select(c => c with { Volume = 0 })).MFI(2).Values,
            new decimal?[5], "MFI zero volume");
        Assert(ReferenceEquals(mfiBars.MFI(2), mfiBars.MFI(2)), "MFI cache reuse");
        Assert(!ReferenceEquals(mfiBars.MFI(2), mfiBars.MFI(3)), "MFI period cache isolation");
        var ruleInstrument = new Instruments(2, "VOLUME");
        ruleInstrument.SetCandles(tf, mfiBars);
        var ruleContext = new EvaluationContext(ruleInstrument, mfiBars[^1].CloseTime);
        Assert(new IndicatorOperand(new IndicatorSpec(IndicatorType.ADL, tf)).Resolve(ruleContext).Value
            == mfiBars.ADL().Values[^1], "ADL rule resolution");
        Assert(new IndicatorOperand(new IndicatorSpec(IndicatorType.MFI, tf, Period1: 2)).Resolve(ruleContext).Value
            == mfiBars.MFI(2).Values[^1], "MFI rule resolution");
        Throws<ArgumentOutOfRangeException>(() => mfiBars.MFI(0));
        Throws<ArgumentOutOfRangeException>(() => mfiBars.MFI(-1));
        Assert(new Candles(tf, Array.Empty<Candle>()).ADL().Count == 0, "Empty ADL");
        Assert(new Candles(tf, Array.Empty<Candle>()).MFI().Count == 0, "Empty MFI");
        Equal(Prices(10, 12).MFI(2).Values, new decimal?[2], "Insufficient MFI history");

        foreach (var positional in new[] { Timeframe.Day, Timeframe.Week, Timeframe.Month })
        {
            // Use interval boundaries so even monthly candles have unique increasing closes.
            var source = new[]
            {
                Candle.Create(Start, positional, 160, 220, 100, 160, 1),
                Candle.Create(Start.AddMonths(2), positional, 180, 240, 120, 180, 1),
                Candle.Create(Start.AddMonths(4), positional, 999, 1000, 998, 999, 1)
            };
            var candles = new Candles(positional, source);
            var p = candles.Camarilla();
            var lines = new[] { p.R4, p.R3, p.R2, p.R1, p.S1, p.S2, p.S3, p.S4 };
            decimal[] expected = { 226, 193, 182, 171, 149, 138, 127, 94 };
            for (int i = 0; i < lines.Length; i++)
            {
                Assert(lines[i][0] == null && lines[i][1] == expected[i]
                    && lines[i][2] == expected[i] + 20, "Camarilla level, lag and output alignment");
                Assert(lines[i].Candles == candles, "Camarilla retains source alignment");
            }
            Assert(ReferenceEquals(p, candles.Camarilla()), "Camarilla cache reuse");
            var prefix = new Candles(positional, source.Take(2));
            Equal(prefix.Camarilla().R4.Values, p.R4.Values[..2], "Camarilla prefix invariance");
            var instrument = new Instruments(1, "TEST");
            instrument.SetCandles(positional, source);
            var operand = new IndicatorOperand(new IndicatorSpec(IndicatorType.Camarilla,
                positional, Component: IndicatorComponent.R3));
            var result = operand.Resolve(new EvaluationContext(instrument, source[1].CloseTime));
            Assert(result.HasValue && result.Value == 193m, "Camarilla rule component resolution");
        }
        Throws<ArgumentException>(() => mfiBars.Camarilla());
        Throws<ArgumentException>(() => CamarillaPivots.Calculate(90, 100, 95));
        Throws<ArgumentException>(() => CamarillaPivots.Calculate(120, 100, 121));
        var flat = CamarillaPivots.Calculate(100, 100, 100);
        Assert(flat.R4 == 100 && flat.S4 == 100, "Camarilla zero range");

        foreach (int length in Enumerable.Range(0, mfiBars.Count + 1))
        {
            var prefix = new Candles(tf, mfiBars.Take(length));
            Equal(prefix.ADL().Values, mfiBars.ADL().Values[..length], "ADL prefix invariance");
            Equal(prefix.MFI(2).Values, mfiBars.MFI(2).Values[..length], "MFI prefix invariance");
        }
        var corrected = new Candles(tf, adlBars.Select((c, i) => i == 0 ? c with { Close = 9 } : c));
        Assert(corrected.ADL()[0] == -50 && adlBars.ADL()[0] == 50, "Corrected history uses independent cache");
        var correctedMfi = new Candles(tf, mfiBars.Select((c, i) => i == 1
            ? c with { Open = 8, High = 8, Low = 8, Close = 8 } : c));
        Assert(correctedMfi.MFI(2)[2] != mfiBars.MFI(2)[2], "MFI corrected history uses independent cache");
        Assert(IndicatorRequirements.GetMinimumBars(new IndicatorSpec(IndicatorType.ADL)) == 1, "ADL history requirement");
        Assert(IndicatorRequirements.GetMinimumBars(new IndicatorSpec(IndicatorType.MFI, Period1: 2)) == 3, "MFI history requirement");
        Assert(IndicatorRequirements.GetMinimumBars(new IndicatorSpec(IndicatorType.Camarilla)) == 2, "Camarilla history requirement");
        VerifyScannerRequirements();
        Console.WriteLine($"Passed {assertions} Stage1 ADL, MFI and Camarilla assertions.");
    }

    private void VerifyScannerRequirements()
    {
        var scanner = new Scanner("New indicator dependencies")
        {
            TimeFrameInheritable = true, RootTimeFrame = Timeframe.Minutes(15)
        };
        scanner.BullishConditions.Add(Rule.Create(new IndicatorOperand(
            new IndicatorSpec(IndicatorType.ADL)), RuleOperator.GreaterThan, 0m));
        scanner.BullishConditions.Add(Rule.Create(new IndicatorOperand(
            new IndicatorSpec(IndicatorType.MFI, Period1: 14), Offset: 2), RuleOperator.GreaterThan, 50m));
        scanner.BullishConditions.Add(Rule.Create(new IndicatorOperand(
            new IndicatorSpec(IndicatorType.Camarilla, Timeframe.Day, Component: IndicatorComponent.S4)),
            RuleOperator.GreaterThan, 0m));
        var requirements = scanner.GetRequirements();
        Assert(requirements.MinimumBarsRequired[Timeframe.Minutes(15)] == 17, "MFI warmup plus offset dependency");
        Assert(requirements.MinimumBarsRequired[Timeframe.Day] == 2, "Camarilla positional dependency");
        var invalid = new Scanner("Invalid intraday Camarilla") { TimeFrameInheritable = true, RootTimeFrame = Timeframe.Minutes(15) };
        invalid.BullishConditions.Add(Rule.Create(new IndicatorOperand(
            new IndicatorSpec(IndicatorType.Camarilla, Component: IndicatorComponent.R1)), RuleOperator.GreaterThan, 0m));
        Throws<InvalidOperationException>(() => invalid.GetRequirements());
        var daily = new Candles(Timeframe.Day, new[]
        {
            Bar(0, Timeframe.Day, 220, 100, 160, 1),
            Candle.Create(Start.AddDays(1), Timeframe.Day, 180, 240, 120, 180, 1)
        });
        var instrument = new Instruments(3, "PIVOTS");
        instrument.SetCandles(Timeframe.Day, daily);
        IndicatorComponent[] components = { IndicatorComponent.R4, IndicatorComponent.R3, IndicatorComponent.R2,
            IndicatorComponent.R1, IndicatorComponent.S1, IndicatorComponent.S2, IndicatorComponent.S3, IndicatorComponent.S4 };
        decimal[] expected = { 226, 193, 182, 171, 149, 138, 127, 94 };
        for (int i = 0; i < components.Length; i++)
        {
            var operand = new IndicatorOperand(new IndicatorSpec(IndicatorType.Camarilla,
                Timeframe.Day, Component: components[i]));
            Assert(!operand.Resolve(new EvaluationContext(instrument, daily[0].CloseTime)).HasValue,
                "Camarilla warmup remains unknown even with future rows");
            var value = operand.Resolve(new EvaluationContext(instrument, daily[1].CloseTime));
            Assert(value.HasValue && value.Value == expected[i], "Every Camarilla rule selector maps correctly");
        }
        Throws<ArgumentException>(() => new IndicatorOperand(new IndicatorSpec(IndicatorType.Camarilla, Timeframe.Day))
            .Resolve(new EvaluationContext(instrument, daily[1].CloseTime)));
    }

    private static Candle Bar(int index, Timeframe tf, decimal high, decimal low, decimal close, decimal volume)
        => Candle.Create(Start.AddMinutes(15 * index), tf, close, high, low, close, volume);
    private static Candles Prices(params decimal[] prices)
        => new(Timeframe.Minutes(15), prices.Select((c, i) => Bar(i, Timeframe.Minutes(15), c, c, c, i + 1)));
    private void Assert(bool condition, string message)
    {
        assertions++;
        if (!condition) throw new Exception(message);
    }
    private void Equal(decimal?[] actual, decimal?[] expected, string message)
        => Assert(actual.SequenceEqual(expected), message);
    private void Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { assertions++; return; }
        throw new Exception($"Expected {typeof(T).Name}.");
    }
}
