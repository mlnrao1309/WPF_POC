using AlgoTrading.Backtest.Metrics;
using AlgoTrading.Backtest.Runtime;
using AlgoTrading.Models;

namespace AlgoTrading.Backtest.Results;

public sealed class BacktestResult
{
    public int SchemaVersion { get; init; } = 1;
    public ReproducibilityRecord Reproducibility { get; init; } = new();
    public BacktestMetrics Metrics { get; init; } = new();
    public IReadOnlyList<Trade> Trades { get; init; } = Array.Empty<Trade>();
    public IReadOnlyList<EquityPoint> EquityCurve { get; init; } = Array.Empty<EquityPoint>();
    public SweepResult? Sweep { get; set; }
    public WalkForwardResult? WalkForward { get; set; }
    public RiskAssessment RiskAssessment { get; set; } = new();
}

public sealed class RiskAssessment
{
    public RiskAssessmentStatus Status { get; init; }
    public RiskAssessmentReason Reason { get; init; } = RiskAssessmentReason.InsufficientData;
    public RiskLossBasis LossBasis { get; init; }
    public decimal TradedCapital { get; init; }
    public decimal DailyLossLimit { get; init; }
    public decimal MonthlyLossLimit { get; init; }
    public bool? IsBacktestApproved => Status switch
    {
        RiskAssessmentStatus.Approved => true,
        RiskAssessmentStatus.Rejected => false,
        _ => null
    };
    public IReadOnlyList<CapitalPoint> CapitalCurve { get; init; } = Array.Empty<CapitalPoint>();
    public IReadOnlyList<RiskPeriodSummary> Periods { get; init; } = Array.Empty<RiskPeriodSummary>();
    public IReadOnlyList<RiskLimitBreach> Breaches { get; init; } = Array.Empty<RiskLimitBreach>();
}

public sealed class CapitalPoint
{
    public DateTime DateTime { get; init; }
    public decimal RealizedCapital { get; init; }
    public decimal Equity { get; init; }
}

public sealed class RiskPeriodSummary
{
    public RiskPeriod Period { get; init; }
    public DateOnly StartDateIst { get; init; }
    public decimal OpeningRealizedCapital { get; init; }
    public decimal ClosingRealizedCapital { get; set; }
    public decimal OpeningEquity { get; init; }
    public decimal ClosingEquity { get; set; }
    public decimal RealizedPnL => ClosingRealizedCapital - OpeningRealizedCapital;
    public decimal EquityPnL => ClosingEquity - OpeningEquity;
    public decimal Limit { get; init; }
    public DateTime? FirstBreachAtUtc { get; set; }
}

public sealed class RiskLimitBreach
{
    public RiskPeriod Period { get; init; }
    public DateOnly StartDateIst { get; init; }
    public DateTime AtUtc { get; init; }
    public decimal Loss { get; init; }
    public decimal Limit { get; init; }
}
