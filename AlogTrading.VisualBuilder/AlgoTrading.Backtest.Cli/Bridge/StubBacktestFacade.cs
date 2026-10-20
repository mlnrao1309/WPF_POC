using System.Globalization;
using System.Text;

namespace AlgoTrading.Backtest.Cli.Bridge;

/// <summary>
/// Placeholder facade that keeps the T7 file pipeline (CLI + daemon + result
/// writing) fully exercisable without T3/T6. Produces a well-formed empty
/// BacktestResult JSON, an empty trade CSV header, and a human summary.
/// Replace with a real IBacktestFacade once the engine is wired.
/// </summary>
public sealed class StubBacktestFacade : IBacktestFacade
{
    public FacadeResult Run(FacadeRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        return FacadeResult.Ok(
            BuildResultJson(request),
            BuildEmptyTradesCsv(),
            BuildSummary(request),
            new RunMetricsSummary());
    }

    private static string BuildResultJson(FacadeRequest req)
    {
        var sb = new StringBuilder(256);
        sb.Append('{');
        sb.Append("\"schemaVersion\":1,");
        sb.Append("\"reproducibility\":{");
        sb.Append("\"instrumentToken\":").Append(req.InstrumentToken.ToString(CultureInfo.InvariantCulture)).Append(',');
        sb.Append("\"dataFrom\":\"").Append(req.FromUtc.ToString("o", CultureInfo.InvariantCulture)).Append("\",");
        sb.Append("\"dataTo\":\"").Append(req.ToUtc.ToString("o", CultureInfo.InvariantCulture)).Append("\",");
        sb.Append("\"engineVersion\":\"stub\"");
        sb.Append("},");
        sb.Append("\"metrics\":{");
        sb.Append("\"totalTrades\":0,\"netPnl\":0,\"winRate\":0,\"sharpe\":0,\"maxDrawdownPct\":0");
        sb.Append("},");
        sb.Append("\"trades\":[],");
        sb.Append("\"equityCurve\":[],");
        sb.Append("\"sweep\":null,");
        sb.Append("\"walkForward\":null");
        sb.Append('}');
        return sb.ToString();
    }

    private static string BuildEmptyTradesCsv() =>
        "TradeId,Direction,EntryTime,ExitTime,EntryPrice,ExitPrice,Quantity," +
        "GrossPnL,Brokerage,STT,ExchangeTxn,SEBI,StampDuty,GST,TotalCosts,NetPnL," +
        "MAE,MFE,BarsHeld,DaysHeld,ExitReason\r\n";

    private static string BuildSummary(FacadeRequest req)
    {
        var sb = new StringBuilder(256);
        sb.Append("Strategy:   ").AppendLine(req.StrategyName);
        sb.Append("Instrument: ").AppendLine(req.InstrumentToken.ToString(CultureInfo.InvariantCulture));
        sb.Append("From:       ").AppendLine(req.FromUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        sb.Append("To:         ").AppendLine(req.ToUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        sb.Append("Mode:       ").AppendLine(req.Mode.ToString());
        sb.AppendLine();
        sb.AppendLine("Total trades: 0");
        sb.AppendLine("Net P&L:      0");
        sb.AppendLine();
        sb.AppendLine("NOTE: Stub engine. Provide a real IBacktestFacade implementation after T3/T6 land.");
        return sb.ToString();
    }
}