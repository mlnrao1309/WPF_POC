using System.Text.Json;
using AlgoTrading.Models;

namespace AlgoTrading.RuleEditor.Services;

/// <summary>
/// Non-typed analysis over a RuleDefinition. Walks the serialized JSON so
/// it is robust against the exact T1 object model for Condition / Group.
/// </summary>
public static class RuleAnalysisService
{
    public static IReadOnlyCollection<string> CollectReferencedTimeframes(RuleDefinition rule)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);

        try
        {
            var json = RuleDefinitionJson.Serialize(rule);
            using var doc = JsonDocument.Parse(json);
            WalkForTimeframes(doc.RootElement, set);
        }
        catch
        {
        }

        try
        {
            var evalTf = rule.EvaluationTimeframe.ToString();
            if (!string.IsNullOrEmpty(evalTf)) set.Add(evalTf);
        }
        catch { }

        try
        {
            var baseTf = rule.BaseTimeframe.ToString();
            if (!string.IsNullOrEmpty(baseTf)) set.Add(baseTf);
        }
        catch { }

        return set;
    }

    private static void WalkForTimeframes(JsonElement el, HashSet<string> set)
    {
        switch (el.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var prop in el.EnumerateObject())
                {
                    if (prop.NameEquals("timeframe") &&
                        prop.Value.ValueKind == JsonValueKind.String)
                    {
                        var v = prop.Value.GetString();
                        if (!string.IsNullOrEmpty(v)) set.Add(v);
                    }
                    WalkForTimeframes(prop.Value, set);
                }
                break;

            case JsonValueKind.Array:
                foreach (var item in el.EnumerateArray())
                    WalkForTimeframes(item, set);
                break;
        }
    }
}