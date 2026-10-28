using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using AlgoTrading.Backtest;
using AlgoTrading.Backtest.Metrics;
using AlgoTrading.Backtest.Results;
using AlgoTrading.Backtest.Runtime;
using AlgoTrading.Data;
using AlgoTrading.Data.Daos;
using AlgoTrading.Models;

namespace AlgoTrading.Backtest.Cli.Bridge;

/// <summary>
/// Real IBacktestFacade implementation. This is the sole file in the CLI
/// project that references AlgoTrading.Backtest.*, AlgoTrading.Data.* and
/// AlgoTrading.Models.*. Everything else in the CLI stays decoupled.
///
/// Pipeline:
///   1. Deserialize the rule from request JSON.
///   2. Validate it via RuleDefinitionValidator.
///   3. Load bars through BarSeriesFactory (backed by AppSettings.Data).
///   4. Build BacktestEngineOptions from AppSettings + request.
///   5. Run the base engine on the full series.
///   6. Attach sweep / walk-forward blocks per request.Mode.
///   7. Serialize: result JSON, trades CSV, summary text, metrics summary.
///
/// Exceptions are mapped to FacadeResult.Fail; OperationCanceledException is
/// re-thrown so the CLI's cancellation surface keeps working.
/// </summary>
public sealed class BacktestFacade : IBacktestFacade
{
    private readonly AppSettings _settings;
    private readonly BacktestRunsDao _runs;
    private readonly BacktestArtifactDao _artifacts;
    public BacktestFacade(AppSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _runs = new BacktestRunsDao(settings.Data.Sql.ConnectionString);
        _artifacts = new BacktestArtifactDao(settings.Data.Sql.ConnectionString);
    }

    public FacadeResult Run(FacadeRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        bool historyStarted = false;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var rule = RuleDefinitionJson.Deserialize(request.RuleJson);
            var validation = RuleDefinitionValidator.Validate(rule);
            if (!validation.IsValid)
                return FacadeResult.Fail("Validation", string.Join("; ", validation.Errors.Select(e => e.ToString())));
            if (!string.Equals(_settings.Data.Source, "Sql", StringComparison.OrdinalIgnoreCase))
                return FacadeResult.Fail("DataPreparation", "Strict backtest preparation requires the SQL data source.");
            var prepared = SqlBarSeries.Prepare(_settings.Data.Sql.ConnectionString, request.InstrumentToken,
                request.RuleJson, request.FromUtc, request.ToUtc);
            cancellationToken.ThrowIfCancellationRequested();
            StartRun(request, prepared);
            historyStarted = true;
            var result = RunCore(request, rule, prepared, cancellationToken);
            if (!result.Success)
                FailRun(request.RunId, result.ErrorType + ": " + result.ErrorMessage);
            return result;
        }
        catch (OperationCanceledException)
        {
            if (historyStarted) _runs.FailRun(request.RunId, "Backtest cancelled.", cancelled: true);
            throw;
        }
        catch (JsonException ex)
        {
            return RecordFailure(request.RunId, historyStarted, "InvalidJson", ex.Message);
        }
        catch (Exception ex)
        {
            return RecordFailure(request.RunId, historyStarted, ex.GetType().Name, ex.Message);
        }
    }

    private void StartRun(FacadeRequest request, IReadOnlyDictionary<Timeframe, IBarSeries> prepared)
    {
        using var rule = JsonDocument.Parse(request.RuleJson);
        string configuration = JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            rule = rule.RootElement,
            runMode = request.Mode.ToString(),
            engineVersion = EngineInfo.Version,
            initialCapital = request.TradedCapital,
            lossBasis = request.LossBasis.ToString(),
            dailyLossLimitPercent = 2m,
            monthlyLossLimitPercent = 5m,
            preparedData = prepared.Select(p => new
            {
                timeframe = p.Key.ToString(),
                sourceTimeframe = (p.Value as SqlBarSeries)?.AggregatedFrom?.ToString(),
                candles = p.Value.Count,
                fromUtc = p.Value.From,
                toUtc = p.Value.To
            }),
            riskFreeRatePercent = _settings.Backtest.RiskFreeRatePercent,
            quantity = 1,
            costsJson = ReadConfigOrEmpty(_settings.Configs.CostsPath),
            slippageJson = ReadConfigOrEmpty(_settings.Configs.SlippagePath),
            marginsJson = ReadConfigOrEmpty(_settings.Configs.MarginsPath)
        });
        string version = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(request.RuleJson)));
        string executionMode = rule.RootElement.TryGetProperty("executionMode", out var mode)
            ? mode.GetString() ?? "Delivery" : "Delivery";
        _runs.StartRun(request.RunId, request.StrategyName, version, request.InstrumentToken,
            _settings.Data.Source, "NextBarOpen/" + executionMode,
            new DateTimeOffset(DateTime.SpecifyKind(request.FromUtc, DateTimeKind.Utc)),
            new DateTimeOffset(DateTime.SpecifyKind(request.ToUtc, DateTimeKind.Utc)),
            configuration, request.TradedCapital);
    }

    public void CompleteRun(Guid runId, string resultJson, string resultFilePath)
        => _runs.CompleteRun(runId, resultJson, resultFilePath);

    public void FailRun(Guid runId, string errorMessage)
        => _runs.FailRun(runId, errorMessage);

    private FacadeResult RecordFailure(Guid runId, bool historyStarted, string errorType, string message)
    {
        if (historyStarted)
        {
            try { FailRun(runId, message); }
            catch (Exception ex) { message += " | Failed to update run history: " + ex.Message; }
        }
        return FacadeResult.Fail(errorType, message);
    }

    private FacadeResult RunCore(FacadeRequest req, RuleDefinition rule,
        IReadOnlyDictionary<Timeframe, IBarSeries> prepared, CancellationToken ct)
    {
        // Preparation has succeeded before the history row is created.
        var bars = prepared[rule.BaseTimeframe];

        var signalObservations = new List<SignalObservation>();
        bool recordSignals = true;

        // 4. Engine options.
        var options = new BacktestEngineOptions
        {
            SignalObserver = observation => { if (recordSignals) signalObservations.Add(observation); },
            InitialCapital = req.TradedCapital,
            LossBasis = req.LossBasis,
            RiskFreeRatePercent = _settings.Backtest.RiskFreeRatePercent,
            Quantity = 1,
            MaxParallelism = req.MaxParallelism,
            CostsJson = ReadConfigOrEmpty(_settings.Configs.CostsPath),
            SlippageJson = ReadConfigOrEmpty(_settings.Configs.SlippagePath),
            MarginsJson = ReadConfigOrEmpty(_settings.Configs.MarginsPath),
            DataSource = _settings.Data.Source,
            RunId = req.RunId,
            PreparedSeries = prepared,
            DailySeries = prepared.GetValueOrDefault(Timeframe.Parse("D"))
        };

        // 5. Base run on the full series.
        var engine = new BacktestEngine(options);
        var result = engine.Run(rule, bars, ct);
        recordSignals = false;

        // 7. --- Generate Replay Artifacts ---
        var replayGen = new ReplayArtifactGenerator();
        var artifacts = replayGen.Generate(req.RunId, rule, prepared, bars, signalObservations);
        _artifacts.InsertArtifactsAsync(req.RunId, artifacts).GetAwaiter().GetResult();

        // 6. Mode-specific blocks.
        switch (req.Mode)
        {
            case RunMode.Sweep:
                result.Sweep = ParameterSweep.Run(
                    rule, bars, options, SweepCriterion.Sharpe,
                    _settings.Backtest.MaxSweepCombinations, ct);
                break;

            case RunMode.WalkForward:
                result.WalkForward = WalkForward.Run(
                    rule, bars, options,
                    new WalkForwardConfig
                    {
                        InSampleRatio = _settings.Backtest.WalkForward.InSampleRatio,
                        WindowCount = _settings.Backtest.WalkForward.WindowCount
                    },
                    _settings.Backtest.MaxSweepCombinations, ct);
                break;

            case RunMode.Single:
            default:
                break;
        }

        // 7. Serialize artifacts.
        if (req.Mode != RunMode.Single)
            result.RiskAssessment = new RiskAssessment
            {
                Status = RiskAssessmentStatus.NotAssessed,
                Reason = RiskAssessmentReason.UnsupportedRunMode,
                LossBasis = req.LossBasis,
                TradedCapital = req.TradedCapital,
                DailyLossLimit = req.TradedCapital * 0.02m,
                MonthlyLossLimit = req.TradedCapital * 0.05m
            };
        string resultJson = BacktestResultJson.Serialize(result);
        string tradesCsv = BuildTradesCsv(result.Trades);
        string summary = BuildSummary(req, result);
        var metricsSummary = SelectMetricsSummary(req.Mode, result);

        return FacadeResult.Ok(resultJson, tradesCsv, summary, metricsSummary);
    }

    private string ReadConfigOrEmpty(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) return "";
        string full = AppSettingsLoader.ResolvePath(_settings, relativePath);
        return File.Exists(full) ? File.ReadAllText(full) : "";
    }

    private static RunMetricsSummary SelectMetricsSummary(RunMode mode, BacktestResult result)
    {
        BacktestMetrics m = mode switch
        {
            RunMode.Sweep when result.Sweep is not null
                => result.Sweep.Best.Metrics,
            RunMode.WalkForward when result.WalkForward is not null
                => result.WalkForward.AggregateOOSMetrics,
            _ => result.Metrics
        };

        return new RunMetricsSummary
        {
            TotalTrades = m.TotalTrades,
            NetPnL = m.TotalNetPnL,
            WinRatePct = m.WinRatePct,
            Sharpe = m.Sharpe,
            MaxDrawdownPct = m.MaxDrawdownPct
        };
    }

    private static string BuildTradesCsv(IReadOnlyList<Trade> trades)
    {
        var sb = new StringBuilder(256 + trades.Count * 128);
        sb.Append("TradeId,Direction,EntryTime,ExitTime,EntryPrice,ExitPrice,Quantity,");
        sb.Append("GrossPnL,Brokerage,STT,ExchangeTxn,SEBI,StampDuty,GST,TotalCosts,NetPnL,");
        sb.Append("MAE,MFE,BarsHeld,DaysHeld,ExitReason\r\n");

        var inv = CultureInfo.InvariantCulture;
        foreach (var t in trades)
        {
            sb.Append(t.TradeId.ToString(inv)).Append(',');
            sb.Append(t.Direction.ToString()).Append(',');
            sb.Append(t.EntryTime.ToString("o", inv)).Append(',');
            sb.Append(t.ExitTime.ToString("o", inv)).Append(',');
            sb.Append(t.EntryPrice.ToString(inv)).Append(',');
            sb.Append(t.ExitPrice.ToString(inv)).Append(',');
            sb.Append(t.Quantity.ToString(inv)).Append(',');
            sb.Append(t.GrossPnL.ToString(inv)).Append(',');
            sb.Append(t.Brokerage.ToString(inv)).Append(',');
            sb.Append(t.STT.ToString(inv)).Append(',');
            sb.Append(t.ExchangeTxn.ToString(inv)).Append(',');
            sb.Append(t.SEBI.ToString(inv)).Append(',');
            sb.Append(t.StampDuty.ToString(inv)).Append(',');
            sb.Append(t.GST.ToString(inv)).Append(',');
            sb.Append(t.TotalCosts.ToString(inv)).Append(',');
            sb.Append(t.NetPnL.ToString(inv)).Append(',');
            sb.Append(t.MAE.ToString(inv)).Append(',');
            sb.Append(t.MFE.ToString(inv)).Append(',');
            sb.Append(t.BarsHeld.ToString(inv)).Append(',');
            sb.Append(t.DaysHeld.ToString(inv)).Append(',');
            sb.Append(t.ExitReason.ToString()).Append("\r\n");
        }
        return sb.ToString();
    }

    private static string BuildSummary(FacadeRequest req, BacktestResult result)
    {
        var m = result.Metrics;
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder(512);

        sb.Append("Strategy:   ").AppendLine(req.StrategyName);
        sb.Append("Instrument: ").AppendLine(req.InstrumentToken.ToString(inv));
        sb.Append("From:       ").AppendLine(req.FromUtc.ToString("yyyy-MM-dd", inv));
        sb.Append("To:         ").AppendLine(req.ToUtc.ToString("yyyy-MM-dd", inv));
        sb.Append("Mode:       ").AppendLine(req.Mode.ToString());
        sb.AppendLine();

        sb.Append("Total trades:    ").AppendLine(m.TotalTrades.ToString(inv));
        sb.Append("Net P&L:         ").AppendLine(m.TotalNetPnL.ToString(inv));
        sb.Append("Win rate:        ").Append(m.WinRatePct.ToString("0.##", inv)).AppendLine("%");
        sb.Append("Profit factor:   ").AppendLine(m.ProfitFactor.ToString("0.####", inv));
        sb.Append("Sharpe:          ").AppendLine(m.Sharpe.ToString("0.####", inv));
        sb.Append("Max drawdown:    ").Append(m.MaxDrawdownPct.ToString("0.##", inv)).AppendLine("%");
        sb.AppendLine();

        var risk = result.RiskAssessment;
        sb.Append("Loss basis:      ").AppendLine(risk.LossBasis.ToString());
        sb.Append("Traded capital:  INR ").AppendLine(risk.TradedCapital.ToString("N2", inv));
        sb.Append("Risk assessment: ").Append(risk.Status).Append(" / ").AppendLine(risk.Reason.ToString());
        sb.Append("Loss limits:     Daily INR ").Append(risk.DailyLossLimit.ToString("N2", inv))
            .Append("; monthly INR ").AppendLine(risk.MonthlyLossLimit.ToString("N2", inv));
        sb.AppendLine("Monthly P&L (IST; realized net / change in marked equity):");
        foreach (var month in risk.Periods.Where(p => p.Period == RiskPeriod.Monthly))
            sb.Append("  ").Append(month.StartDateIst.ToString("yyyy-MM", inv)).Append("  INR ")
                .Append(month.RealizedPnL.ToString("N2", inv)).Append(" / ")
                .AppendLine(month.EquityPnL.ToString("N2", inv));
        foreach (var breach in risk.Breaches)
            sb.Append("  Breach ").Append(breach.Period).Append(" at ")
                .Append(breach.AtUtc.AddMinutes(330).ToString("yyyy-MM-dd HH:mm", inv))
                .Append(" IST; loss INR ").Append(breach.Loss.ToString("N2", inv))
                .Append("; limit INR ").AppendLine(breach.Limit.ToString("N2", inv));
        sb.AppendLine("Assessment only; StrategyMain.IsApproved remains a user decision.");
        sb.AppendLine();

        if (req.Mode == RunMode.Sweep && result.Sweep is not null)
        {
            var s = result.Sweep;
            sb.Append("Sweep criterion: ").AppendLine(s.Criterion.ToString());
            sb.Append("Combinations:    ").AppendLine(s.CombinationCount.ToString(inv));
            sb.Append("Best criterion:  ").AppendLine(s.Best.CriterionValue.ToString("0.####", inv));
            sb.AppendLine();
            sb.AppendLine("Best parameters:");
            foreach (var kv in s.Best.Parameters.OrderBy(k => k.Key, StringComparer.Ordinal))
                sb.Append("  ").Append(kv.Key).Append(" = ").AppendLine(kv.Value.ToString(inv));
            sb.AppendLine();
            sb.AppendLine("Top 10 ranked:");
            int take = Math.Min(10, s.Ranked.Count);
            for (int i = 0; i < take; i++)
            {
                sb.Append("  #").Append((i + 1).ToString(inv))
                  .Append("  criterion=").Append(s.Ranked[i].CriterionValue.ToString("0.####", inv));
                foreach (var kv in s.Ranked[i].Parameters.OrderBy(k => k.Key, StringComparer.Ordinal))
                    sb.Append("  ").Append(kv.Key).Append('=').Append(kv.Value.ToString(inv));
                sb.AppendLine();
            }
        }
        else if (req.Mode == RunMode.WalkForward && result.WalkForward is not null)
        {
            var w = result.WalkForward;
            sb.Append("Windows:                     ").AppendLine(w.Windows.Count.ToString(inv));
            sb.Append("Aggregate OOS Sharpe:        ").AppendLine(w.AggregateOOSSharpe.ToString("0.####", inv));
            sb.Append("Aggregate OOS ProfitFactor:  ").AppendLine(w.AggregateOOSProfitFactor.ToString("0.####", inv));
            sb.Append("Aggregate OOS MaxDD%:        ").AppendLine(w.AggregateOOSMaxDrawdownPct.ToString("0.##", inv));
            sb.Append("Consistency:                 ").AppendLine(w.ConsistencyScore.ToString("0.####", inv));
            sb.AppendLine();
            sb.AppendLine("Per window:");
            foreach (var win in w.Windows)
            {
                sb.Append("  #").Append(win.WindowIndex.ToString(inv))
                  .Append("  OOS trades=").Append(win.OOSMetrics.TotalTrades.ToString(inv))
                  .Append("  OOS Sharpe=").Append(win.OOSMetrics.Sharpe.ToString("0.####", inv))
                  .Append("  Degradation=").AppendLine(win.DegradationRatio.ToString("0.####", inv));
            }
        }

        return sb.ToString();
    }
}
