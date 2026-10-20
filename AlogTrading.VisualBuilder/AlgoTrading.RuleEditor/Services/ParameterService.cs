using System.Text.Json;
using System.Text.Json.Nodes;
using AlgoTrading.Models;
using AlgoTrading.RuleEditor.Models;

namespace AlgoTrading.RuleEditor.Services;

public static class ParameterService
{
    public static List<EditorParameter> Load(RuleDefinition rule)
    {
        var list = new List<EditorParameter>();
        try
        {
            var json = RuleDefinitionJson.Serialize(rule);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("parameters", out var arr)) return list;
            if (arr.ValueKind != JsonValueKind.Array) return list;
            foreach (var el in arr.EnumerateArray())
            {
                var p = new EditorParameter
                {
                    Name = el.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String
                        ? n.GetString() ?? "" : "",
                    Default = GetDecimal(el, "default"),
                    Minimum = GetDecimal(el, "minimum"),
                    Maximum = GetDecimal(el, "maximum"),
                    Step = GetDecimal(el, "step", 1m)
                };
                list.Add(p);
            }
        }
        catch { }
        return list;
    }

    public static RuleDefinition Apply(RuleDefinition rule, IEnumerable<EditorParameter> parameters)
    {
        var json = RuleDefinitionJson.Serialize(rule);
        var obj = JsonNode.Parse(json)?.AsObject();
        if (obj is null) return rule;

        var arr = new JsonArray();
        foreach (var p in parameters)
        {
            arr.Add(new JsonObject
            {
                ["name"] = p.Name,
                ["default"] = p.Default,
                ["minimum"] = p.Minimum,
                ["maximum"] = p.Maximum,
                ["step"] = p.Step
            });
        }
        if (arr.Count == 0) obj.Remove("parameters");
        else obj["parameters"] = arr;

        return RuleDefinitionJson.Deserialize(obj.ToJsonString());
    }

    private static decimal GetDecimal(JsonElement el, string name, decimal dflt = 0m)
    {
        if (!el.TryGetProperty(name, out var v)) return dflt;
        return v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out var d) ? d : dflt;
    }
}