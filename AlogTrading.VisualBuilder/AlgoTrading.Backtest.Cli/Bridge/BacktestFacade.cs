using System.Globalization;
using System.Text;
using System.Text.Json;
using AlgoTrading.Backtest;
using AlgoTrading.Backtest.Metrics;
using AlgoTrading.Backtest.Results;
using AlgoTrading.Backtest.Runtime;
using AlgoTrading.Data;
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

    public BacktestFacade(AppSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    public FacadeResult Run(FacadeRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            return RunCore(request, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (JsonException ex)
        {
            return FacadeResult.Fail("InvalidJson", ex.Message);
        }
        catch (Exception ex)
        {
            return FacadeResult.Fail(ex.GetType().Name, ex.Message);
        }
    }

    private FacadeResult RunCore(FacadeRequest req, CancellationToken ct)
    {
        // 1. Rule.
        var rule = RuleDefinitionJson.Deserialize(req.RuleJson);

        // 2. Validate.
        var validation = RuleDefinitionValidator.Validate(rule);
        if (!validation.IsValid)
        {
            string msg = string.Join("; ", validation.Errors.Select(e => e.ToString()));
            return FacadeResult.Fail("Validation", msg);
        }

        // 3. Load bars.
        var dataOptions = new DataOptions
        {
            Source = _settings.Data.Source,
            Sql = new SqlOptions { ConnectionString = _settings.Data.Sql.ConnectionString },
            Csv = new CsvOptions { FolderPath = _settings.Data.Csv.FolderPath },
            Parquet = new ParquetOptions { FolderPath = _settings.Data.Parquet.FolderPath }
        };
        var factory = new BarSeriesFactory(dataOptions);
        var bars = factory.Load(req.InstrumentToken, rule.EvaluationTimeframe,
                                req.FromUtc, req.ToUtc);

        // 4. Engine options.
        var options = new BacktestEngineOptions
        {
            InitialCapital = _settings.Backtest.InitialCapital,
            RiskFreeRatePercent = _settings.Backtest.RiskFreeRatePercent,
            Quantity = 1,
            MaxParallelism = req.MaxParallelism,
            CostsJson = ReadConfigOrEmpty(_settings.Configs.CostsPath),
            SlippageJson = ReadConfigOrEmpty(_settings.Configs.SlippagePath),
            MarginsJson = ReadConfigOrEmpty(_settings.Configs.MarginsPath),
            DataSource = _settings.Data.Source,
            RunId = Guid.NewGuid(),
            DailySeries = (bars as SqlBarSeries)?.DailySeries
        };

        // 5. Base run on the full series.
        var engine = new BacktestEngine(options);
        var result = engine.Run(rule, bars, ct);

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
                        WindowCount   = _settings.Backtest.WalkForward.WindowCount
                    },
                    _settings.Backtest.MaxSweepCombinations, ct);
                break;

            case RunMode.Single:
            default:
                break;
        }

        // 7. Serialize artifacts.
        string resultJson = BacktestResultJson.Serialize(result);
        string tradesCsv  = BuildTradesCsv(result.Trades);
        string summary    = BuildSummary(req, result);
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
            TotalTrades    = m.TotalTrades,
            NetPnL         = m.TotalNetPnL,
            WinRatePct     = m.WinRatePct,
            Sharpe         = m.Sharpe,
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
