using System;
using AlgoTrading.Aggregation;
using AlgoTrading.Data;
using AlgoTrading.Models;

namespace AlgoTrading.Backtest.Tests;

internal static class Tfs
{
    private static Timeframe? _daily;
    public static Timeframe Daily => _daily ??= Probe("D");
    private static Timeframe Probe(string s)
    {
        string json = $$$"""
{
  "schemaVersion": 1,
  "strategyName": "p",
  "direction": "Long",
  "evaluationTimeframe": "{{s}}",
  "executionMode": "Delivery",
  "baseTimeframe": "{{m}}",
  "entry": {
    "kind": "group",
    "enabled": true,
    "logic": "All",
    "children": [
      {
        "kind": "condition",
        "enabled": true,
        "left": { "kind": "field", "offset": 0, "field": "Close" },
        "operator": "GreaterThan",
        "right": { "kind": "number", "value": 1 }
      }
    ]
  },
  "stopLoss": { "mode": "Percentage", "value": 2 }
}
""";


        return RuleDefinitionJson.Deserialize(json).BaseTimeframe;
    }
}

internal static class Th
{
    public static IBarSeries Series(decimal[] o, decimal[] h, decimal[] l, decimal[] c)
    {
        int n = c.Length;
        var v = new decimal[n];
        var oi = new decimal[n];
        var dt = new DateTime[n];
        var start = new DateTime(2024, 1, 1, 9, 15, 0, DateTimeKind.Utc);
        for (int i = 0; i < n; i++) dt[i] = start.AddDays(i);
        return new TestBarSeries(1u, "TEST", Tfs.Daily, o, h, l, c, v, oi, dt);
    }

    public static RuleDefinition Rule(
        string direction = "Long",
        decimal threshold = 100m,
        string? slMode = "Percentage", decimal? slValue = 2m,
        string? tpMode = "Percentage", decimal? tpValue = 10m,
        string execMode = "Delivery")
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("{\"schemaVersion\":1,\"strategyName\":\"Test\",");
        sb.Append("\"direction\":\"").Append(direction).Append("\",");
        sb.Append("\"evaluationTimeframe\":\"D\",\"executionMode\":\"").Append(execMode).Append("\",");
        sb.Append("\"baseTimeframe\":\"D\",");
        sb.Append("\"entry\":{\"kind\":\"group\",\"enabled\":true,\"logic\":\"All\",\"children\":[");
        sb.Append("{\"kind\":\"condition\",\"enabled\":true,");
        sb.Append("\"left\":{\"kind\":\"field\",\"offset\":0,\"field\":\"Close\"},");
        sb.Append("\"operator\":\"GreaterThan\",");
        sb.Append("\"right\":{\"kind\":\"number\",\"value\":")
          .Append(threshold.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append("}}]}");
        if (slMode is not null && slValue.HasValue)
            sb.Append(",\"stopLoss\":{\"mode\":\"").Append(slMode).Append("\",\"value\":")
              .Append(slValue.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append('}');
        if (tpMode is not null && tpValue.HasValue)
            sb.Append(",\"target\":{\"mode\":\"").Append(tpMode).Append("\",\"value\":")
              .Append(tpValue.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append('}');
        sb.Append('}');
        return RuleDefinitionJson.Deserialize(sb.ToString());
    }

    public static BacktestEngine Engine(RuleDefinition rule, IBarSeries series, BacktestConfig? cfg = null, decimal slip = 0m)
    {
        cfg ??= new BacktestConfig { InitialCapital = 100000m, FixedQuantity = 1 };
        var cost = new IndianEquityCostModel(new CostConfig());
        var slipModel = new FixedSlippageModel(slip);
        var margin = new EquityMarginModel(new MarginConfig());
        var cache = new TimeframeCache(series);
        return new BacktestEngine(rule, series.InstrumentToken, series, cache, cfg, cost, slipModel, margin);
    }
}