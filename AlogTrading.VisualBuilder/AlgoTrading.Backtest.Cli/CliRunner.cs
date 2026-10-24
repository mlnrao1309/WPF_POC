using System.Globalization;
using System.Text.Json;
using AlgoTrading.Backtest.Cli.Bridge;

namespace AlgoTrading.Backtest.Cli;

public static class CliRunner
{
    public static int Run(AppSettings settings, CliLogger logger, CliArguments args,
                          IBacktestFacade facade, CancellationToken cancellationToken)
    {
        FacadeRequest? request = null;
        try
        {
            logger.Information("Loading rule: " + Path.GetFileName(args.RulePath!));

            string ruleJson;
            try { ruleJson = File.ReadAllText(args.RulePath!); }
            catch (Exception ex) { logger.Error("Failed to read rule file.", ex); return 1; }

            string strategyName;
            try { strategyName = ExtractStrategyName(ruleJson); }
            catch (Exception ex) { logger.Error("Rule JSON is invalid.", ex); return 1; }

            logger.Information("Validating... OK");

            var fromUtc = DateTime.SpecifyKind(args.From.ToDateTime(TimeOnly.MinValue).AddMinutes(-330), DateTimeKind.Utc);
            var toUtc = DateTime.SpecifyKind(args.To.ToDateTime(new TimeOnly(23, 59, 59)).AddMinutes(-330), DateTimeKind.Utc);

            var mode = args.Sweep ? RunMode.Sweep
                     : args.WalkForward ? RunMode.WalkForward
                     : RunMode.Single;

            logger.Information("Running backtest: token=" + args.Instrument +
                               ", from=" + args.From + ", to=" + args.To + ", mode=" + mode);

            request = new FacadeRequest
            {
                RuleJson = ruleJson,
                StrategyName = strategyName,
                InstrumentToken = args.Instrument,
                FromUtc = fromUtc,
                ToUtc = toUtc,
                Mode = mode,
                MaxParallelism = settings.Backtest.MaxParallelism
            };

            FacadeResult result;
            try { result = facade.Run(request, cancellationToken); }
            catch (OperationCanceledException) { logger.Warning("Backtest cancelled."); return 130; }
            catch (Exception ex) { logger.Error("Backtest failed.", ex); return 1; }

            if (!result.Success)
            {
                logger.Error("Backtest failed: " + result.ErrorType + ": " + result.ErrorMessage);
                return 1;
            }

            var resultsFolder = !string.IsNullOrWhiteSpace(args.OutFolder)
                ? Path.GetFullPath(args.OutFolder!)
                : AppSettingsLoader.ResolvePath(settings, settings.Paths.Results);

            var writer = new ResultWriter(resultsFolder);
            var baseName = ResultWriter.BuildBaseName(
                strategyName, args.Instrument, args.From, args.To, DateTime.UtcNow);
            var outcome = writer.Write(baseName, result);
            facade.CompleteRun(request.RunId, result.ResultJson!, outcome.ResultPath);

            var m = result.MetricsSummary;
            if (m is not null)
            {
                var line = string.Create(CultureInfo.InvariantCulture,
                    $"Backtest complete: {m.TotalTrades} trades, Net P&L={m.NetPnL}, " +
                    $"WinRate={m.WinRatePct}%, Sharpe={m.Sharpe}, MaxDD={m.MaxDrawdownPct}%");
                logger.Information(line);
            }
            logger.Information("Result: " + outcome.ResultPath);
            return 0;
        }
        catch (Exception ex)
        {
            if (request is not null)
            {
                try { facade.FailRun(request.RunId, ex.Message); }
                catch (Exception historyError) { logger.Error("Failed to record run failure.", historyError); }
            }
            logger.Error("Unhandled error in CLI runner.", ex);
            return 1;
        }
    }

    private static string ExtractStrategyName(string ruleJson)
    {
        using var doc = JsonDocument.Parse(ruleJson);
        if (doc.RootElement.ValueKind == JsonValueKind.Object &&
            doc.RootElement.TryGetProperty("strategyName", out var nameEl) &&
            nameEl.ValueKind == JsonValueKind.String)
        {
            var name = nameEl.GetString();
            if (!string.IsNullOrWhiteSpace(name)) return name;
        }
        return "strategy";
    }
}
