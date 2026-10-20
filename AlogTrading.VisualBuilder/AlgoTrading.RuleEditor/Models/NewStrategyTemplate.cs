namespace AlgoTrading.RuleEditor.Models;

/// <summary>
/// A minimal, valid rule JSON used to seed a New strategy. Using a template
/// string avoids guessing the T1 object model for ConditionGroup / Condition
/// construction; we just hand the template to the T1 deserializer.
/// One trivial entry condition (Close > 0) satisfies RULE_009 so a new
/// strategy is immediately saveable.
/// </summary>
public static class NewStrategyTemplate
{
    public const string Json = """
    {
      "schemaVersion": 1,
      "strategyName": "Untitled strategy",
      "direction": "Long",
      "evaluationTimeframe": "D",
      "executionMode": "Delivery",
      "baseTimeframe": "D",
      "entry": {
        "kind": "group",
        "enabled": true,
        "logic": "All",
        "children": [
          {
            "kind": "condition",
            "enabled": true,
            "left":  { "kind": "field", "offset": 0, "field": "Close" },
            "operator": "GreaterThan",
            "right": { "kind": "number", "value": 0 }
          }
        ]
      },
      "stopLoss": { "mode": "Percentage", "value": 2.0 },
      "target":   { "mode": "Percentage", "value": 4.0 }
    }
    """;
}