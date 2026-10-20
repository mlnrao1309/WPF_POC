using System;

namespace AlgoTrading.Backtest;

public sealed class BacktestConfig
{
    public decimal InitialCapital { get; set; } = 100000m;
    public int FixedQuantity { get; set; } = 1;
    public decimal RiskFreeRatePercent { get; set; } = 2.0m;
    public int MaxSweepCombinations { get; set; } = 10000;
    public SweepCriterion DefaultSweepCriterion { get; set; } = SweepCriterion.Sharpe;
    public WalkForwardConfig WalkForward { get; set; } = new WalkForwardConfig();
}

public sealed class WalkForwardConfig
{
    public decimal InSampleRatio { get; set; } = 0.7m;
    public int WindowCount { get; set; } = 5;
}

public enum SweepCriterion { Sharpe, ProfitFactor, NetPnL, Calmar }

public sealed class CostConfig
{
    public decimal BrokeragePerOrder { get; set; } = 0m;
    public decimal SttBuyPct { get; set; } = 0.1m;
    public decimal SttSellPct { get; set; } = 0.1m;
    public decimal ExchangeTxnPct { get; set; } = 0.00345m;
    public decimal SebiPerCrore { get; set; } = 10m;
    public decimal StampDutyBuyPct { get; set; } = 0.015m;
    public decimal GstPct { get; set; } = 18m;
    public IntradayCostOverrides Intraday { get; set; } = new IntradayCostOverrides();
}

public sealed class IntradayCostOverrides
{
    public decimal BrokeragePerOrder { get; set; } = 20m;
    public decimal SttSellPct { get; set; } = 0.025m;
    public decimal StampDutyBuyPct { get; set; } = 0.003m;
}

public sealed class SlippageConfig
{
    public SlippageMode Mode { get; set; } = SlippageMode.Spread;
    public decimal FixedAmount { get; set; } = 0m;
    public decimal SpreadPercent { get; set; } = 0.05m;
}

public enum SlippageMode { Fixed, Spread }

public sealed class MarginConfig
{
    public decimal DeliveryMarginPct { get; set; } = 100m;
    public decimal IntradayLeverage { get; set; } = 5m;
}