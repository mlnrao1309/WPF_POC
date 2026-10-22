using System.Text.Json.Serialization;

namespace AlgoTrading.DataAccess.Etl;

public class ScannerBlueprintDto
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("timeframe")]
    public string Timeframe { get; set; } = string.Empty;

    [JsonPropertyName("indicators")]
    public List<IndicatorRequirementDto> Indicators { get; set; } = new();
}

public class IndicatorRequirementDto
{
    [JsonPropertyName("identifier")]
    public string Identifier { get; set; } = string.Empty; // e.g., "EMA(s.Close, 5)"

    [JsonPropertyName("indicator")]
    public string Indicator { get; set; } = string.Empty; // e.g., "EMA"

    [JsonPropertyName("inputs")]
    public List<string> Inputs { get; set; } = new(); // e.g., ["Close"]

    [JsonPropertyName("parameters")]
    public List<string> Parameters { get; set; } = new(); // e.g., ["5"]
}