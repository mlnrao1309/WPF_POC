using System;
using AlgoTrading.Aggregation;
using AlgoTrading.Data;
using AlgoTrading.Models;
using Xunit;

namespace AlgoTrading.Backtest.Tests;

public class MarginRejectionTests
{
    // Bar 0: signal-eligible close. Bar 1: next-bar open executes entry.
    // Price 1000 => notional 1000, delivery margin 1000.
    // We hand cfg a starting capital of 500 so entry must be rejected.
    private static readonly decimal[] O = { 1000m, 1000m, 1000m, 1000m, 1000m };
    private static readonly decimal[] H = { 1001m, 1001m, 1001m, 1001m, 1001m };
    private static readonly decimal[] L = {  999m,  999m,  999m,  999m,  999m };
    private static readonly decimal[] C = { 1000m, 1000m, 1000m, 1000m, 1000m };

    private static RuleDefinition AlwaysEntry()
    {
        string json = """
        {
          "schemaVersion": 1,
          "strategyName": "AlwaysEntry",
          "direction": "Long",
          "evaluationTimeframe": "D",
          "executionMode": "Delivery",
          "baseTimeframe": "D",
          "entry": {
            "kind": "group", "enabled": true, "logic": "All",
            "children": [{
              "kind": "condition", "enabled": true,
              "left":  { "kind": "field", "offset": 0, "field": "Close" },
              "operator": "GreaterThan",
              "right": { "kind": "number", "value": 1 }
            }]
          },
          "stopLoss": { "mode": "Percentage", "value": 50 }
        }
        """;
        return RuleDefinitionJson.Deserialize(json);
    }

    [Fact]
    public void InsufficientMargin_RecordsRejection_AndTakesNoTrade()
    {
        var rule = AlwaysEntry();
        var series = Th.Series(O, H, L, C);
        var cfg = new BacktestConfig
        {
            InitialCapital = 500m,       // less than 1000 required
            FixedQuantity = 1,
        };
        var cost  = new IndianEquityCostModel(new CostConfig());
        var slip  = new FixedSlippageModel(0m);
        var margin = new EquityMarginModel(new MarginConfig());
        var cache = new TimeframeCache(series);
        var engine = new BacktestEngine(rule, series.InstrumentToken, series, cache, cfg, cost, slip, margin);

        var r = engine.Run();

        Assert.Empty(r.Trades);
        Assert.NotEmpty(r.RejectedEntries);
        var rej = r.RejectedEntries[0];
        Assert.Equal("Insufficient margin", rej.Reason);
        Assert.True(rej.RequiredMargin > rej.AvailableCapital);
        Assert.Equal(500m, rej.AvailableCapital);
        Assert.Equal(1000m, rej.RequiredMargin);  // delivery margin 100%
    }

    [Fact]
    public void SufficientMargin_TakesTrade_NoRejections()
    {
        var rule = AlwaysEntry();
        var series = Th.Series(O, H, L, C);
        var cfg = new BacktestConfig { InitialCapital = 5000m, FixedQuantity = 1 };
        var cost  = new IndianEquityCostModel(new CostConfig());
        var slip  = new FixedSlippageModel(0m);
        var margin = new EquityMarginModel(new MarginConfig());
        var cache = new TimeframeCache(series);
        var engine = new BacktestEngine(rule, series.InstrumentToken, series, cache, cfg, cost, slip, margin);

        var r = engine.Run();

        Assert.Empty(r.RejectedEntries);
        Assert.NotEmpty(r.Trades);
    }

    [Fact]
    public void IntradayMargin_UsesLeverage_NotFullNotional()
    {
        var rule = AlwaysEntry();
        var series = Th.Series(O, H, L, C);

        // Intraday 5x leverage => 1000 / 5 = 200 required. 500 available is enough.
        string json = """
        {
          "schemaVersion": 1,
          "strategyName": "IntradayEntry",
          "direction": "Long",
          "evaluationTimeframe": "D",
          "executionMode": "Intraday",
          "baseTimeframe": "D",
          "entry": {
            "kind": "group", "enabled": true, "logic": "All",
            "children": [{
              "kind": "condition", "enabled": true,
              "left":  { "kind": "field", "offset": 0, "field": "Close" },
              "operator": "GreaterThan",
              "right": { "kind": "number", "value": 1 }
            }]
          },
          "stopLoss": { "mode": "Percentage", "value": 50 }
        }
        """;
        var intradayRule = RuleDefinitionJson.Deserialize(json);

        var cfg = new BacktestConfig { InitialCapital = 500m, FixedQuantity = 1 };
        var cost  = new IndianEquityCostModel(new CostConfig());
        var slip  = new FixedSlippageModel(0m);
        var margin = new EquityMarginModel(new MarginConfig());
        var cache = new TimeframeCache(series);
        var engine = new BacktestEngine(intradayRule, series.InstrumentToken, series, cache, cfg, cost, slip, margin);

        var r = engine.Run();

        // 500 > 200 required â†’ trade taken, no rejections.
        Assert.NotEmpty(r.Trades);
        Assert.Empty(r.RejectedEntries);
    }
}