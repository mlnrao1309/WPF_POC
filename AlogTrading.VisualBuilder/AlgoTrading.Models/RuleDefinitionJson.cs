using System.Text.Json;
using System.Text.Json.Serialization;

namespace AlgoTrading.Models;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = true,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(RuleDefinition))]
[JsonSerializable(typeof(ConditionGroup))]
[JsonSerializable(typeof(Condition))]
[JsonSerializable(typeof(ValueExpression))]
[JsonSerializable(typeof(FieldExpression))]
[JsonSerializable(typeof(NumberExpression))]
[JsonSerializable(typeof(IndicatorExpression))]
[JsonSerializable(typeof(ParameterExpression))]
[JsonSerializable(typeof(CalculationExpression))]
[JsonSerializable(typeof(ParameterSpec))]
[JsonSerializable(typeof(StopLossSpec))]
[JsonSerializable(typeof(TargetSpec))]
public partial class RuleDefinitionJsonContext : JsonSerializerContext
{
}

public static class RuleDefinitionJson
{
    private static readonly JsonSerializerOptions Options = BuildOptions();

    private static JsonSerializerOptions BuildOptions()
    {
        var o = new JsonSerializerOptions(RuleDefinitionJsonContext.Default.Options)
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip
        };
        o.Converters.Add(new JsonStringEnumConverter());
        o.Converters.Add(new TimeframeJsonConverter());
        o.Converters.Add(new NullableTimeframeJsonConverter());
        return o;
    }

    public static RuleDefinition Deserialize(string json)
        => JsonSerializer.Deserialize<RuleDefinition>(json, Options)
           ?? throw new JsonException("Rule JSON deserialized to null.");

    public static string Serialize(RuleDefinition rule)
        => JsonSerializer.Serialize(rule, Options);

    public static RuleDefinition LoadFromFile(string path)
        => Deserialize(File.ReadAllText(path));

    public static void SaveToFile(RuleDefinition rule, string path)
    {
        var utf8 = new System.Text.UTF8Encoding(false);
        File.WriteAllText(path, Serialize(rule), utf8);
    }
}
