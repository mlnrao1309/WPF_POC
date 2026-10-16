using System.Text.Json.Serialization;

namespace AlgoTrading.Dtos;

public sealed class MarketWatchApiResponse
{
    [JsonPropertyName("data")]
    public MarketWatchApiData? Data { get; set; }
}

public sealed class MarketWatchApiData
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("items")]
    public List<MarketWatchApiItem> Items { get; set; } = [];
}

public sealed class MarketWatchApiItem
{
    [JsonPropertyName("instrument_token")]
    public long InstrumentToken { get; set; }

    [JsonPropertyName("tradingsymbol")]
    public string TradingSymbol { get; set; } = string.Empty;

    [JsonPropertyName("exchange")]
    public string Exchange { get; set; } = string.Empty;
}
