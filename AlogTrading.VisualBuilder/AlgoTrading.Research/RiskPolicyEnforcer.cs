using AlgoTrading.Backtest.Metrics;

public sealed record RiskPolicyConfig
{
    public int MinCompletedTrades { get; init; } = 5;
    public decimal MaxAllowableDrawdownPct { get; init; } = 25.0m;
    public decimal MinSharpeRatio { get; init; } = 0.0m;
}

public sealed record RiskPolicyEvaluationResult
{
    public required bool IsCompliant { get; init; }
    public IReadOnlyList<string> ViolatedConstraints { get; init; } = Array.Empty<string>();
    public required string Reason { get; init; }
}

public static class RiskPolicyEnforcer
{
    /// <summary>
    /// Enforces explicit M3 risk and sample adequacy constraints on a candidate backtest result (M3-14).
    /// </summary>
    public static RiskPolicyEvaluationResult EvaluatePolicy(BacktestMetrics metrics, RiskPolicyConfig? config = null)
    {
        ArgumentNullException.ThrowIfNull(metrics);
        config ??= new RiskPolicyConfig();

        var violations = new List<string>();

        if (metrics.TotalTrades < config.MinCompletedTrades)
        {
            violations.Add($"MinCompletedTrades ({metrics.TotalTrades} < {config.MinCompletedTrades})");
        }

        if (metrics.MaxDrawdownPct > config.MaxAllowableDrawdownPct)
        {
            violations.Add($"MaxAllowableDrawdownPct ({metrics.MaxDrawdownPct}% > {config.MaxAllowableDrawdownPct}%)");
        }

        if (metrics.Sharpe < config.MinSharpeRatio)
        {
            violations.Add($"MinSharpeRatio ({metrics.Sharpe} < {config.MinSharpeRatio})");
        }

        if (violations.Count > 0)
        {
            return new RiskPolicyEvaluationResult
            {
                IsCompliant = false,
                ViolatedConstraints = violations,
                Reason = $"Candidate violated {violations.Count} risk/sample constraint(s): {string.Join(", ", violations)}."
            };
        }

        return new RiskPolicyEvaluationResult
        {
            IsCompliant = true,
            ViolatedConstraints = Array.Empty<string>(),
            Reason = "Candidate complies with all risk and sample adequacy policies."
        };
    }
}