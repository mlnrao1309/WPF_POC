using System.Globalization;
using System.Text.Json;
using AlgoTrading.Backtest.Cli.Bridge;

namespace AlgoTrading.Backtest.Cli;

public sealed class JobOutcome
{
    public bool Success { get; init; }
    public string? ResultBaseName { get; init; }
    public string? ErrorMessage { get; init; }

    public static JobOutcome Ok(string baseName) => new() { Success = true, ResultBaseName = baseName };
    public static JobOutcome Fail(string message) => new() { Success = false, ErrorMessage = message };
}

/// <summary>
/// Runs one job: load job file, load companion strategy, call facade,
/// write results. Errors are captured in the outcome, not thrown.
/// </summary>
public sealed class JobProcessor
{
    private readonly CliLogger _logger;
    private readonly IBacktestFacade _facade;
    private readonly ResultWriter _writer;
    private readonly int _maxParallelism;

    public JobProcessor(CliLogger logger, IBacktestFacade facade, ResultWriter writer, int maxParallelism)
    {
        _logger = logger;
        _facade = facade;
        _writer = writer;
        _maxParallelism = maxParallelism;
    }

    public JobOutcome Process(string jobPath, string strategyFileName)
    {
        FacadeRequest? request = null;
        try
        {
            var job = JobFile.Load(jobPath);
            var jobDir = Path.GetDirectoryName(jobPath) ?? ".";
            var strategyPath = Path.Combine(jobDir, strategyFileName);

            if (!File.Exists(strategyPath))
                return JobOutcome.Fail("Strategy file not found: " + strategyFileName);

            var ruleJson = File.ReadAllText(strategyPath);
            var strategyName = ExtractStrategyName(ruleJson);

            var fromUtc = DateTime.SpecifyKind(job.From.ToDateTime(TimeOnly.MinValue).AddMinutes(-330), DateTimeKind.Utc);
            var toUtc = DateTime.SpecifyKind(job.To.ToDateTime(new TimeOnly(23, 59, 59)).AddMinutes(-330), DateTimeKind.Utc);

            var mode = job.Mode switch
            {
                JobMode.Single => RunMode.Single,
                JobMode.Sweep => RunMode.Sweep,
                JobMode.WalkForward => RunMode.WalkForward,
                _ => RunMode.Single
            };

            request = new FacadeRequest
            {
                RuleJson = ruleJson,
                StrategyName = strategyName,
                InstrumentToken = job.InstrumentToken,
                FromUtc = fromUtc,
                ToUtc = toUtc,
                Mode = mode,
                LossBasis = job.LossBasis,
                TradedCapital = job.TradedCapital,
                MaxParallelism = _maxParallelism
            };

            // Current job must not be interrupted by Ctrl+C (Section 6.10).
            var result = _facade.Run(request, CancellationToken.None);
            if (!result.Success)
                return JobOutcome.Fail(result.ErrorType + ": " + result.ErrorMessage);

            var baseName = ResultWriter.BuildBaseName(
                strategyName, job.InstrumentToken, job.From, job.To, DateTime.UtcNow);
            var outcome = _writer.Write(baseName, result);
            _facade.CompleteRun(request.RunId, result.ResultJson!, outcome.ResultPath);

            var m = result.MetricsSummary;
            if (m is not null)
            {
                var line = string.Create(CultureInfo.InvariantCulture,
                    $"Backtest complete: strategy={strategyName}, token={job.InstrumentToken}, " +
                    $"trades={m.TotalTrades}, netPnL={m.NetPnL}, winRate={m.WinRatePct}%, " +
                    $"sharpe={m.Sharpe}, maxDD={m.MaxDrawdownPct}%");
                _logger.Information(line);
            }
            _logger.Debug("Wrote result: " + outcome.ResultPath);
            return JobOutcome.Ok(baseName);
        }
        catch (Exception ex)
        {
            if (request is not null)
            {
                try { _facade.FailRun(request.RunId, ex.Message); }
                catch (Exception historyError) { _logger.Error("Failed to record run failure.", historyError); }
            }
            return JobOutcome.Fail(ex.GetType().Name + ": " + ex.Message);
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
