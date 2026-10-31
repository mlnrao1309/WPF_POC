using System.Text.Json;
using System.Text.Json.Nodes;
using AlgoTrading.Models;
using AlgoTrading.RuleEditor.Models;

namespace AlgoTrading.RuleEditor.Services;

public static class ConditionTreeService
{
    private static readonly string[] Slots = { "eligibility", "entry", "protection", "exit", "fullExit" };

    public static bool IsSlot(string slot)
    {
        foreach (var s in Slots) if (string.Equals(s, slot, StringComparison.Ordinal)) return true;
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
        catch { return null; }
    }

    public static RuleDefinition ApplySlot(RuleDefinition rule, string slot, EditorNode? root)
    {
        if (!IsSlot(slot)) return rule;
        var json = RuleDefinitionJson.Serialize(rule);
        var obj = JsonNode.Parse(json)?.AsObject();
        if (obj is null) return rule;

        if (root is null || ShouldPrune(root, slot)) obj.Remove(slot);
        else obj[slot] = NodeToJson(root);

        return RuleDefinitionJson.Deserialize(obj.ToJsonString());
    }

    private static bool ShouldPrune(EditorNode root, string slot)
    {
        if (slot == "entry") return false;
        if (root.Kind != "group") return false;
        if (root.Children.Count > 0) return false;
        if (!string.IsNullOrEmpty(root.Name)) return false;
        if (!root.Enabled) return false;
        if (!string.Equals(root.Logic, "All", StringComparison.Ordinal)) return false;
        return true;
    }

    public static List<EditorReferenceBlock> LoadReferences(RuleDefinition rule)
    {
        var list = new List<EditorReferenceBlock>();
        try
        {
            var json = RuleDefinitionJson.Serialize(rule);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("references", out var arr) || arr.ValueKind != JsonValueKind.Array) return list;

            foreach (var el in arr.EnumerateArray())
            {
                var block = new EditorReferenceBlock();

                // --- MIGRATION: Check if loading an old format file ---
                if (el.TryGetProperty("id", out var oldId))
                {
                    block.Name = oldId.GetString() ?? "RefBlock";

                    var oldCapture = new EditorReferenceCapture { Id = block.Name };
                    if (el.TryGetProperty("value", out var oldVal) && oldVal.ValueKind != JsonValueKind.Null)
                        oldCapture.Value = ExprFromJson(oldVal);
                    else
                        oldCapture.Value = new EditorExpression { Kind = "field", Field = "Close" };

                    block.Captures.Add(oldCapture);
                }
                else
                {
                    block.Name = GetString(el, "name") ?? "RefBlock";
                }

                block.Timeframe = GetString(el, "timeframe") ?? "15m";

                if (el.TryGetProperty("trigger", out var trig))
                    block.Trigger = NodeFromJson(trig, "group") ?? new EditorNode { Kind = "group", Logic = "All" };

                if (el.TryGetProperty("clearOn", out var clr) && clr.ValueKind != JsonValueKind.Null)
                    block.ClearOn = NodeFromJson(clr, "group");

                // Load new format captures (if they exist)
                if (el.TryGetProperty("captures", out var caps) && caps.ValueKind == JsonValueKind.Array)
                {
                    foreach (var cap in caps.EnumerateArray())
                    {
                        var capture = new EditorReferenceCapture { Id = GetString(cap, "id") ?? "" };
                        if (cap.TryGetProperty("value", out var val) && val.ValueKind != JsonValueKind.Null)
                            capture.Value = ExprFromJson(val);
                        block.Captures.Add(capture);
                    }
                }
                list.Add(block);
            }
        }
        catch { }
        return list;
    }

    public static RuleDefinition ApplyReferences(RuleDefinition rule, List<EditorReferenceBlock> blocks)
    {
        var json = RuleDefinitionJson.Serialize(rule);
        var obj = JsonNode.Parse(json)?.AsObject();
        if (obj is null) return rule;

        if (blocks.Count == 0) obj.Remove("references");
        else
        {
            var arr = new JsonArray();
            foreach (var block in blocks)
            {
                var blockObj = new JsonObject { ["name"] = block.Name, ["timeframe"] = block.Timeframe };
                if (block.Trigger is not null) blockObj["trigger"] = NodeToJson(block.Trigger);
                if (block.ClearOn is not null) blockObj["clearOn"] = NodeToJson(block.ClearOn);

                var capsArr = new JsonArray();
                foreach (var cap in block.Captures)
                {
                    capsArr.Add(new JsonObject { ["id"] = cap.Id, ["value"] = ExprToJson(cap.Value) });
                }
                blockObj["captures"] = capsArr;
                arr.Add(blockObj);
            }
            obj["references"] = arr;
        }
        return RuleDefinitionJson.Deserialize(obj.ToJsonString());
    }

    private static EditorNode? NodeFromJson(JsonElement el, string defaultKind = "group")
    {
        if (el.ValueKind != JsonValueKind.Object) return null;
        var kind = GetString(el, "kind") ?? defaultKind;

        // Fallback inference if kind is missing in old files
        if (kind == defaultKind && el.TryGetProperty("left", out _)) kind = "condition";
        else if (kind == defaultKind && el.TryGetProperty("children", out _)) kind = "group";

        if (kind == "group")
        {
            var g = new EditorNode { Kind = "group", Enabled = GetBool(el, "enabled", true), Logic = GetString(el, "logic") ?? "All", Name = GetString(el, "name") };
            if (el.TryGetProperty("children", out var ch) && ch.ValueKind == JsonValueKind.Array)
                foreach (var c in ch.EnumerateArray()) { var n = NodeFromJson(c); if (n is not null) g.Children.Add(n); }
            return g;
        }

        if (kind == "condition")
        {
            return new EditorNode
            {
                Kind = "condition",
                Enabled = GetBool(el, "enabled", true),
                Comment = GetString(el, "comment"),
                Operator = GetString(el, "operator") ?? "GreaterThan",
                Left = el.TryGetProperty("left", out var l) ? ExprFromJson(l) : new EditorExpression(),
                Right = el.TryGetProperty("right", out var r) ? ExprFromJson(r) : new EditorExpression()
            };
        }
        return null;
    }

    private static JsonNode NodeToJson(EditorNode node)
    {
        if (node.Kind == "group")
        {
            var obj = new JsonObject { ["kind"] = "group", ["enabled"] = node.Enabled, ["logic"] = node.Logic };
            if (!string.IsNullOrEmpty(node.Name)) obj["name"] = node.Name;
            var arr = new JsonArray();
            foreach (var c in node.Children) arr.Add(NodeToJson(c));
            obj["children"] = arr;
            return obj;
        }

        var cond = new JsonObject { ["kind"] = "condition", ["enabled"] = node.Enabled };
        if (!string.IsNullOrEmpty(node.Comment)) cond["comment"] = node.Comment;
        cond["left"] = ExprToJson(node.Left);
        cond["operator"] = node.Operator;
        cond["right"] = ExprToJson(node.Right);
        return cond;
    }

    private static EditorExpression ExprFromJson(JsonElement el)
    {
        var expr = new EditorExpression();
        if (el.ValueKind != JsonValueKind.Object) return expr;
        var kind = GetString(el, "kind") ?? "field";
        expr.Kind = kind;
        switch (kind)
        {
            case "field":
                expr.Offset = GetInt(el, "offset", 0); expr.Timeframe = GetString(el, "timeframe"); expr.Field = GetString(el, "field") ?? "Close"; break;
            case "number":
                expr.Number = GetDecimal(el, "value", 0m); break;
            case "parameter":
                expr.ParameterName = GetString(el, "name"); break;
            case "reference":
                expr.ReferenceId = GetString(el, "id"); expr.ReferenceField = GetString(el, "field") ?? "High"; break;
            case "indicator":
                expr.Offset = GetInt(el, "offset", 0); expr.Timeframe = GetString(el, "timeframe"); expr.Indicator = GetString(el, "indicator") ?? "Sma"; expr.Output = GetString(el, "output");
                if (expr.Indicator == "Camarilla") { expr.Indicator = "PivotPointsCamarilla"; if (expr.Timeframe is not ("D" or "W" or "M")) expr.Timeframe = "D"; }
                if (expr.Indicator == "PivotPointsClassic") { if (expr.Timeframe is not ("D" or "W" or "M")) expr.Timeframe = "D"; if (expr.Output == "P") expr.Output = "PP"; }
                if (expr.Indicator == "Count")
                {
                    expr.Lookback = GetInt(el, "lookback", 0); expr.Streak = GetBool(el, "streak", false);
                    if (el.TryGetProperty("predicate", out var predicate)) expr.Predicate = NodeFromJson(predicate, "condition");
                }
                if (el.TryGetProperty("source", out var src) && src.ValueKind != JsonValueKind.Null) expr.Source = ExprFromJson(src);
                if (el.TryGetProperty("params", out var prm) && prm.ValueKind == JsonValueKind.Object)
                    foreach (var p in prm.EnumerateObject()) expr.Params[p.Name] = p.Value.ValueKind == JsonValueKind.Number ? p.Value.GetDecimal() : 0m;
                break;
            case "calculation":
                expr.CalcOp = GetString(el, "op") ?? "Add";
                if (el.TryGetProperty("left", out var l) && l.ValueKind != JsonValueKind.Null) expr.CalcLeft = ExprFromJson(l);
                if (el.TryGetProperty("right", out var r) && r.ValueKind != JsonValueKind.Null) expr.CalcRight = ExprFromJson(r);
                break;
        }
        return expr;
    }

    private static JsonNode ExprToJson(EditorExpression expr)
    {
        switch (expr.Kind)
        {
            case "number": return new JsonObject { ["kind"] = "number", ["value"] = expr.Number };
            case "parameter": return new JsonObject { ["kind"] = "parameter", ["name"] = expr.ParameterName ?? "" };
            case "reference": return new JsonObject { ["kind"] = "reference", ["id"] = expr.ReferenceId ?? "", ["field"] = expr.ReferenceField ?? "High" };
            case "indicator":
                {
                    if (expr.Indicator == "PivotPointsClassic")
                        return new JsonObject { ["kind"] = "indicator", ["offset"] = expr.Offset, ["timeframe"] = expr.Timeframe switch { "W" => "W", "M" => "M", _ => "D" }, ["indicator"] = "PivotPointsClassic", ["output"] = NormalizePivotLevel(expr.Output) };

                    var indicatorKind = expr.Indicator == "PivotPointsCamarilla" ? "Camarilla" : expr.Indicator ?? "Sma";
                    var obj = new JsonObject { ["kind"] = "indicator", ["offset"] = expr.Offset, ["indicator"] = indicatorKind };

                    if (indicatorKind == "Camarilla")
                    {
                        obj["output"] = string.IsNullOrEmpty(expr.Output) ? "R1" : expr.Output;
                        obj["timeframe"] = expr.Timeframe switch { "W" => "W", "M" => "M", _ => "D" };
                        return obj;
                    }

                    if (!string.IsNullOrEmpty(expr.Timeframe)) obj["timeframe"] = expr.Timeframe;
                    if (!string.IsNullOrEmpty(expr.Output)) obj["output"] = expr.Output;
                    if (expr.Source is not null) obj["source"] = ExprToJson(expr.Source);
                    if (indicatorKind == "Count") { obj["lookback"] = expr.Lookback; obj["streak"] = expr.Streak; if (expr.Predicate is not null) obj["predicate"] = NodeToJson(expr.Predicate); }
                    if (expr.Params.Count > 0) { var p = new JsonObject(); foreach (var kv in expr.Params) p[kv.Key] = kv.Value; obj["params"] = p; }
                    return obj;
                }
            case "calculation":
                return new JsonObject { ["kind"] = "calculation", ["op"] = expr.CalcOp ?? "Add", ["left"] = expr.CalcLeft is not null ? ExprToJson(expr.CalcLeft) : new JsonObject { ["kind"] = "number", ["value"] = 0 }, ["right"] = expr.CalcRight is not null ? ExprToJson(expr.CalcRight) : new JsonObject { ["kind"] = "number", ["value"] = 0 } };
            default:
                var fObj = new JsonObject { ["kind"] = "field", ["offset"] = expr.Offset, ["field"] = expr.Field ?? "Close" };
                if (!string.IsNullOrEmpty(expr.Timeframe)) fObj["timeframe"] = expr.Timeframe;
                return fObj;
        }
    }

    private static string NormalizePivotLevel(string? output) => string.IsNullOrEmpty(output) || output == "PP" ? "P" : output!;
    private static string? GetString(JsonElement el, string name) => el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    private static bool GetBool(JsonElement el, string name, bool dflt) => el.TryGetProperty(name, out var v) ? v.ValueKind == JsonValueKind.True : dflt;
    private static int GetInt(JsonElement el, string name, int dflt) => el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : dflt;
    private static decimal GetDecimal(JsonElement el, string name, decimal dflt) => el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out var d) ? d : dflt;
}