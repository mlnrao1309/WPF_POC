namespace AlgoTrading.Backtest.Cli.Bridge;

/// <summary>
/// Output from a backtest facade call. Carries the fully-rendered result
/// artifacts (JSON, CSV, summary) so T7 does not need to know the T6 schema.
/// </summary>
public sealed class FacadeResult
{
    public required bool Success { get; init; }
    public string? ResultJson { get; init; }
    public string? TradesCsv { get; init; }
    public string? SummaryText { get; init; }
    public RunMetricsSummary? MetricsSummary { get; init; }
    public string? ErrorType { get; init; }
    public string? ErrorMessage { get; init; }

    public static FacadeResult Ok(
        string resultJson,
        string tradesCsv,
        string summaryText,
        RunMetricsSummary metrics) =>
        new()
        {
            Success = true,
            ResultJson = resultJson,
            TradesCsv = tradesCsv,
            SummaryText = summaryText,
            MetricsSummary = metrics
        };

    public static FacadeResult Fail(string errorType, string errorMessage) =>
        new() { Success = false, ErrorType = errorType, ErrorMessage = errorMessage };
}