using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AlgoTrading.Backtest;

public static class BacktestResultJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Serialize(BacktestResult r) => JsonSerializer.Serialize(r, Options);

    public static BacktestResult Deserialize(string json)
        => JsonSerializer.Deserialize<BacktestResult>(json, Options)
           ?? throw new InvalidOperationException("Deserialized BacktestResult was null.");

    public static void SaveToFile(BacktestResult r, string path)
        => System.IO.File.WriteAllText(path, Serialize(r), System.Text.Encoding.UTF8);

    public static BacktestResult LoadFromFile(string path)
        => Deserialize(System.IO.File.ReadAllText(path));
}