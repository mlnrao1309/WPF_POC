using System.Text.Json;
using System.Text.Json.Nodes;
using AlgoTrading.Models;
using AlgoTrading.RuleEditor.Models;

namespace AlgoTrading.RuleEditor.Services;

/// <summary>
/// Converts between a RuleDefinition and EditorNode trees by splicing JSON.
/// This keeps the editor decoupled from T1s ConditionGroup / Condition class
/// shapes: the only assumption is that T1 round-trips its own JSON faithfully,
/// which Section 2.15 verification #1 already requires.
/// </summary>
public static class ConditionTreeService
{
    private static readonly string[] Slots =
        { "eligibility", "entry", "protection", "exit", "fullExit" };

    public static bool IsSlot(string slot)
    {
        foreach (var s in Slots)
            if (string.Equals(s, slot, StringComparison.Ordinal)) return true;
        return false;
    }

    public static EditorNode? LoadSlot(RuleDefinition rule, string slot)
    {
        if (!IsSlot(slot)) return null;
        try
        {
            var json = RuleDefinitionJson.Serialize(rule);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty(slot, out var el)) return null;
            if (el.ValueKind == JsonValueKind.Null) return null;
            return NodeFromJson(el);
        }
        catch
        {
            return null;
        }
    }

    public static RuleDefinition ApplySlot(RuleDefinition rule, string slot, EditorNode? root)
    {
        if (!IsSlot(slot)) return rule;
        var json = RuleDefinitionJson.Serialize(rule);
        var obj = JsonNode.Parse(json)?.AsObject();
        if (obj is null) return rule;

        if (root is null || ShouldPrune(root, slot))
            obj.Remove(slot);
        else
            obj[slot] = NodeToJson(root);

        var newJson = obj.ToJsonString();
        return RuleDefinitionJson.Deserialize(newJson);
    }

    private static bool ShouldPrune(EditorNode root, string slot)
    {
        // Entry is mandatory; never prune it.
        if (slot == "entry") return false;
        // Optional slots are pruned when the root is a default-empty group.
        if (root.Kind != "group") return false;
        if (root.Children.Count > 0) return false;
        if (!string.IsNullOrEmpty(root.Name)) return false;
        if (!root.Enabled) return false;
        if (!string.Equals(root.Logic, "All", StringComparison.Ordinal)) return false;
        return true;
    }

    // ---------- Node ----------

    private static EditorNode? NodeFromJson(JsonElement el)
    {
        if (el.ValueKind != JsonValueKind.Object) return null;
        var kind = GetString(el, "kind") ?? "group";

        if (kind == "group")
        {
            var g = new EditorNode
            {
                Kind = "group",
                Enabled = GetBool(el, "enabled", true),
                Logic = GetString(el, "logic") ?? "All",
                Name = GetString(el, "name")
            };
            if (el.TryGetProperty("children", out var ch) && ch.ValueKind == JsonValueKind.Array)
            {
                foreach (var c in ch.EnumerateArray())
                {
                    var n = NodeFromJson(c);
                    if (n is not null) g.Children.Add(n);
                }
            }
            return g;
        }

        if (kind == "condition")
        {
            var c = new EditorNode
            {
                Kind = "condition",
                Enabled = GetBool(el, "enabled", true),
                Comment = GetString(el, "comment"),
                Operator = GetString(el, "operator") ?? "GreaterThan",
                Left = el.TryGetProperty("left", out var l) ? ExprFromJson(l) : new EditorExpression(),
                Right = el.TryGetProperty("right", out var r) ? ExprFromJson(r) : new EditorExpression()
            };
            return c;
        }

        return null;
    }

    private static JsonNode NodeToJson(EditorNode node)
    {
        if (node.Kind == "group")
        {
            var obj = new JsonObject
            {
                ["kind"] = "group",
                ["enabled"] = node.Enabled,
                ["logic"] = node.Logic
            };
            if (!string.IsNullOrEmpty(node.Name)) obj["name"] = node.Name;
            var arr = new JsonArray();
            foreach (var c in node.Children) arr.Add(NodeToJson(c));
            obj["children"] = arr;
            return obj;
        }

        var cond = new JsonObject
        {
            ["kind"] = "condition",
            ["enabled"] = node.Enabled
        };
        if (!string.IsNullOrEmpty(node.Comment)) cond["comment"] = node.Comment;
        cond["left"] = ExprToJson(node.Left);
        cond["operator"] = node.Operator;
        cond["right"] = ExprToJson(node.Right);
        return cond;
    }

    // ---------- Expression ----------

    private static EditorExpression ExprFromJson(JsonElement el)
    {
        var expr = new EditorExpression();
        if (el.ValueKind != JsonValueKind.Object) return expr;
        var kind = GetString(el, "kind") ?? "field";
        expr.Kind = kind;
        switch (kind)
        {
            case "field":
                expr.Offset = GetInt(el, "offset", 0);
                expr.Timeframe = GetString(el, "timeframe");
                expr.Field = GetString(el, "field") ?? "Close";
                break;
            case "number":
                expr.Number = GetDecimal(el, "value", 0m);
                break;
            case "parameter":
                expr.ParameterName = GetString(el, "name");
                break;
            case "indicator":
                expr.Offset = GetInt(el, "offset", 0);
                expr.Timeframe = GetString(el, "timeframe");
                expr.Indicator = GetString(el, "indicator") ?? "Sma";
                expr.Output = GetString(el, "output");
                if (el.TryGetProperty("source", out var src) && src.ValueKind != JsonValueKind.Null)
                    expr.Source = ExprFromJson(src);
                if (el.TryGetProperty("params", out var prm) && prm.ValueKind == JsonValueKind.Object)
                    foreach (var p in prm.EnumerateObject())
                        expr.Params[p.Name] = p.Value.ValueKind == JsonValueKind.Number
                            ? p.Value.GetDecimal() : 0m;
                break;
            case "calculation":
                expr.CalcOp = GetString(el, "op") ?? "Add";
                if (el.TryGetProperty("left", out var l) && l.ValueKind != JsonValueKind.Null)
                    expr.CalcLeft = ExprFromJson(l);
                if (el.TryGetProperty("right", out var r) && r.ValueKind != JsonValueKind.Null)
                    expr.CalcRight = ExprFromJson(r);
                break;
        }
        return expr;
    }

    private static JsonNode ExprToJson(EditorExpression expr)
    {
        switch (expr.Kind)
        {
            case "number":
                return new JsonObject
                {
                    ["kind"] = "number",
                    ["value"] = expr.Number
                };
            case "parameter":
                return new JsonObject
                {
                    ["kind"] = "parameter",
                    ["name"] = expr.ParameterName ?? ""
                };
            case "indicator":
            {
                var obj = new JsonObject
                {
                    ["kind"] = "indicator",
                    ["offset"] = expr.Offset,
                    ["indicator"] = expr.Indicator ?? "Sma"
                };
                if (!string.IsNullOrEmpty(expr.Timeframe)) obj["timeframe"] = expr.Timeframe;
                if (!string.IsNullOrEmpty(expr.Output)) obj["output"] = expr.Output;
                if (expr.Source is not null) obj["source"] = ExprToJson(expr.Source);
                if (expr.Params.Count > 0)
                {
                    var p = new JsonObject();
                    foreach (var kv in expr.Params) p[kv.Key] = kv.Value;
                    obj["params"] = p;
                }
                return obj;
            }
            case "calculation":
            {
                var obj = new JsonObject
                {
                    ["kind"] = "calculation",
                    ["op"] = expr.CalcOp ?? "Add",
                    ["left"] = expr.CalcLeft is not null ? ExprToJson(expr.CalcLeft)
                             : new JsonObject { ["kind"] = "number", ["value"] = 0 },
                    ["right"] = expr.CalcRight is not null ? ExprToJson(expr.CalcRight)
                             : new JsonObject { ["kind"] = "number", ["value"] = 0 }
                };
                return obj;
            }
            default: // field
            {
                var obj = new JsonObject
                {
                    ["kind"] = "field",
                    ["offset"] = expr.Offset,
                    ["field"] = expr.Field ?? "Close"
                };
                if (!string.IsNullOrEmpty(expr.Timeframe)) obj["timeframe"] = expr.Timeframe;
                return obj;
            }
        }
    }

    // ---------- Json helpers ----------

    private static string? GetString(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var v)) return null;
        return v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    }
    private static bool GetBool(JsonElement el, string name, bool dflt)
    {
        if (!el.TryGetProperty(name, out var v)) return dflt;
        return v.ValueKind == JsonValueKind.True ? true
             : v.ValueKind == JsonValueKind.False ? false : dflt;
    }
    private static int GetInt(JsonElement el, string name, int dflt)
    {
        if (!el.TryGetProperty(name, out var v)) return dflt;
        return v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : dflt;
    }
    private static decimal GetDecimal(JsonElement el, string name, decimal dflt)
    {
        if (!el.TryGetProperty(name, out var v)) return dflt;
        return v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out var d) ? d : dflt;
    }
}