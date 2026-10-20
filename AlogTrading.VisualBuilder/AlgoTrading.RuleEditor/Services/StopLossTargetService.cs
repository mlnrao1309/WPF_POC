using System.Text.Json;
using System.Text.Json.Nodes;
using AlgoTrading.Models;

namespace AlgoTrading.RuleEditor.Services;

public sealed class StopLossTargetDto
{
    public string Mode { get; set; } = "Percentage"; // Percentage | Points | Rule
    public decimal Value { get; set; }
    public string? GroupName { get; set; }
}

/// <summary>JSON-splice helpers for stopLoss / target on a RuleDefinition.</summary>
public static class StopLossTargetService
{
    public static StopLossTargetDto? Load(RuleDefinition rule, string slot)
    {
        try
        {
            var json = RuleDefinitionJson.Serialize(rule);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty(slot, out var el)) return null;
            if (el.ValueKind == JsonValueKind.Null) return null;
            var dto = new StopLossTargetDto
            {
                Mode = el.TryGetProperty("mode", out var m) && m.ValueKind == JsonValueKind.String
                    ? m.GetString() ?? "Percentage" : "Percentage"
            };
            if (el.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.Number)
                dto.Value = v.GetDecimal();
            if (el.TryGetProperty("groupName", out var gn) && gn.ValueKind == JsonValueKind.String)
                dto.GroupName = gn.GetString();
            return dto;
        }
        catch { return null; }
    }

    public static RuleDefinition Apply(RuleDefinition rule, string slot, StopLossTargetDto? dto)
    {
        var json = RuleDefinitionJson.Serialize(rule);
        var obj = JsonNode.Parse(json)?.AsObject();
        if (obj is null) return rule;

        if (dto is null)
        {
            obj.Remove(slot);
        }
        else
        {
            var n = new JsonObject { ["mode"] = dto.Mode };
            if (string.Equals(dto.Mode, "Rule", StringComparison.Ordinal))
            {
                n["groupName"] = dto.GroupName ?? "";
            }
            else
            {
                n["value"] = dto.Value;
            }
            obj[slot] = n;
        }
        return RuleDefinitionJson.Deserialize(obj.ToJsonString());
    }
}