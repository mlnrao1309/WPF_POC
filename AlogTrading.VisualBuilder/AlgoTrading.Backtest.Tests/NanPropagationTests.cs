using System;
using AlgoTrading.Models;
using Xunit;

namespace AlgoTrading.Backtest.Tests;

public class NanPropagationTests
{
    // Dec.NaN is the sentinel for "no value" produced by warmup / out-of-range.
    // It must:
    //   - never be treated as a real number in comparisons
    //   - never throw
    //   - propagate through arithmetic to produce another sentinel
    // We assert all three via a public-facing rule that forces warmup NaN.

    [Fact]
    public void Dec_Sentinel_IsMinValue()
    {
        // Direct assertion of the sentinel's contract.
        Assert.Equal(decimal.MinValue, Dec.NaN);
        Assert.True(Dec.IsNaN(Dec.NaN));
        Assert.False(Dec.IsNaN(0m));
        Assert.False(Dec.IsNaN(100m));
    }

    [Fact]
    public void OutOfRangeOffset_ProducesNaN_ComparisonReturnsFalse()
    {
        // Rule requires Close[99] > 0 â€” offset 99 is far beyond a 5-bar series.
        // Every evaluation must return false (not throw, not true).
        string json = """
        {
          "schemaVersion": 1,
          "strategyName": "BigOffset",
          "direction": "Long",
          "evaluationTimeframe": "D",
          "executionMode": "Delivery",
          "baseTimeframe": "D",
          "entry": {
            "kind": "group", "enabled": true, "logic": "All",
            "children": [{
              "kind": "condition", "enabled": true,
              "left":  { "kind": "field", "offset": 99, "field": "Close" },
              "operator": "GreaterThan",
              "right": { "kind": "number", "value": 0 }
            }]
          },
          "stopLoss": { "mode": "Percentage", "value": 50 }
        }
        """;

        var rule = RuleDefinitionJson.Deserialize(json);
        decimal[] o = { 100m, 101m, 102m, 103m, 104m };
        decimal[] h = { 101m, 102m, 103m, 104m, 105m };
        decimal[] l = {  99m, 100m, 101m, 102m, 103m };
        decimal[] c = { 100.5m, 101.5m, 102.5m, 103.5m, 104.5m };

        var r = Th.Engine(rule, Th.Series(o, h, l, c)).Run();

        Assert.Empty(r.Trades);
        Assert.Equal(5, r.EquityCurve.Length);
    }

    [Fact]
    public void DivideByZero_ProducesNaN_ConditionIsFalse_NoException()
    {
        // Close / 0 > 1 â†’ division by zero must produce NaN â†’ condition false.
        string json = """
        {
          "schemaVersion": 1,
          "strategyName": "DivZero",
          "direction": "Long",
          "evaluationTimeframe": "D",
          "executionMode": "Delivery",
          "baseTimeframe": "D",
          "entry": {
            "kind": "group", "enabled": true, "logic": "All",
            "children": [{
              "kind": "condition", "enabled": true,
              "left": {
                "kind": "calculation",
                "op": "Divide",
                "left":  { "kind": "field", "offset": 0, "field": "Close" },
                "right": { "kind": "number", "value": 0 }
              },
              "operator": "GreaterThan",
              "right": { "kind": "number", "value": 1 }
            }]
          },
          "stopLoss": { "mode": "Percentage", "value": 50 }
        }
        """;

        var rule = RuleDefinitionJson.Deserialize(json);
        decimal[] o = { 100m, 101m, 102m, 103m, 104m };
        decimal[] h = { 101m, 102m, 103m, 104m, 105m };
        decimal[] l = {  99m, 100m, 101m, 102m, 103m };
        decimal[] c = { 100.5m, 101.5m, 102.5m, 103.5m, 104.5m };

        var r = Th.Engine(rule, Th.Series(o, h, l, c)).Run();

        Assert.Empty(r.Trades);
    }

    [Fact]
    public void Crossover_WithNaNOnEitherSide_IsFalse_NoException()
    {
        // CrossedAbove with a left offset far beyond data must return false.
        string json = """
        {
          "schemaVersion": 1,
          "strategyName": "CrossNan",
          "direction": "Long",
          "evaluationTimeframe": "D",
          "executionMode": "Delivery",
          "baseTimeframe": "D",
          "entry": {
            "kind": "group", "enabled": true, "logic": "All",
            "children": [{
              "kind": "condition", "enabled": true,
              "left":  { "kind": "field", "offset": 99, "field": "Close" },
              "operator": "CrossedAbove",
              "right": { "kind": "number", "value": 0 }
            }]
          },
          "stopLoss": { "mode": "Percentage", "value": 50 }
        }
        """;

        var rule = RuleDefinitionJson.Deserialize(json);
        decimal[] o = { 100m, 101m, 102m, 103m, 104m };
        decimal[] h = { 101m, 102m, 103m, 104m, 105m };
        decimal[] l = {  99m, 100m, 101m, 102m, 103m };
        decimal[] c = { 100.5m, 101.5m, 102.5m, 103.5m, 104.5m };

        var r = Th.Engine(rule, Th.Series(o, h, l, c)).Run();

        Assert.Empty(r.Trades);
    }

    [Fact]
    public void ModuloByZero_ProducesNaN_ConditionIsFalse()
    {
        string json = """
        {
          "schemaVersion": 1,
          "strategyName": "ModZero",
          "direction": "Long",
          "evaluationTimeframe": "D",
          "executionMode": "Delivery",
          "baseTimeframe": "D",
          "entry": {
            "kind": "group", "enabled": true, "logic": "All",
            "children": [{
              "kind": "condition", "enabled": true,
              "left": {
                "kind": "calculation",
                "op": "Modulo",
                "left":  { "kind": "field", "offset": 0, "field": "Close" },
                "right": { "kind": "number", "value": 0 }
              },
              "operator": "Equal",
              "right": { "kind": "number", "value": 0 }
            }]
          },
          "stopLoss": { "mode": "Percentage", "value": 50 }
        }
        """;

        var rule = RuleDefinitionJson.Deserialize(json);
        decimal[] o = { 100m, 101m, 102m, 103m, 104m };
        decimal[] h = { 101m, 102m, 103m, 104m, 105m };
        decimal[] l = {  99m, 100m, 101m, 102m, 103m };
        decimal[] c = { 100.5m, 101.5m, 102.5m, 103.5m, 104.5m };

        var r = Th.Engine(rule, Th.Series(o, h, l, c)).Run();

        // NaN != NaN in our semantics: NaN conditions are false, not "equal to zero".
        Assert.Empty(r.Trades);
    }
}