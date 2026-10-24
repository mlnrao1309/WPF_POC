using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using AlgoTrading.Models;

namespace AlgoTrading.Backtest.Cli;

public enum JobMode { Single, Sweep, WalkForward }

public sealed class JobFile
{
    public int SchemaVersion { get; init; }
    public string StrategyFile { get; init; } = "";
    public uint InstrumentToken { get; init; }
    public DateOnly From { get; init; }
    public DateOnly To { get; init; }
    public JobMode Mode { get; init; }
    public RiskLossBasis LossBasis { get; init; }
    public decimal TradedCapital { get; init; } = 50_000m;
    public int? StrategyId { get; init; }
    public int? TunedId { get; init; }
    public string SourcePath { get; init; } = "";

    public static JobFile Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var text = File.ReadAllText(path);
        var dto = JsonSerializer.Deserialize<JobFileDto>(text, JobFileJson.Options)
                  ?? throw new InvalidDataException("Empty or null job file.");

        if (dto.SchemaVersion != 1)
            throw new InvalidDataException(
                "Unsupported schemaVersion " + dto.SchemaVersion + " (expected 1).");
        if (string.IsNullOrWhiteSpace(dto.StrategyFile))
            throw new InvalidDataException("strategyFile is required.");
        if (dto.StrategyFile.Contains('/') || dto.StrategyFile.Contains('\\'))
            throw new InvalidDataException("strategyFile must be a bare filename (no path separators).");
        if (dto.InstrumentToken == 0)
            throw new InvalidDataException("instrumentToken must be non-zero.");

        if (!DateOnly.TryParseExact(dto.From, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var from))
            throw new InvalidDataException("Invalid 'from' date: " + dto.From);
        if (!DateOnly.TryParseExact(dto.To, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var to))
            throw new InvalidDataException("Invalid 'to' date: " + dto.To);
        if (from.CompareTo(to) > 0)
            throw new InvalidDataException("'from' (" + from + ") must be <= 'to' (" + to + ").");

        if (!Enum.TryParse<JobMode>(dto.Mode, ignoreCase: true, out var mode))
            throw new InvalidDataException("Invalid mode: " + dto.Mode);
        if (!Enum.IsDefined(dto.LossBasis) || dto.TradedCapital <= 0m)
            throw new InvalidDataException("Invalid loss basis or traded capital.");

        return new JobFile
        {
            SchemaVersion = dto.SchemaVersion,
            StrategyFile = dto.StrategyFile,
            InstrumentToken = dto.InstrumentToken,
            From = from,
            To = to,
            Mode = mode,
            LossBasis = dto.LossBasis,
            TradedCapital = dto.TradedCapital,
            StrategyId = dto.StrategyId,
            TunedId = dto.TunedId,
            SourcePath = Path.GetFullPath(path)
        };
    }
}

internal sealed class JobFileDto
{
    public int SchemaVersion { get; set; }
    public string StrategyFile { get; set; } = "";
    public uint InstrumentToken { get; set; }
    public string From { get; set; } = "";
    public string To { get; set; } = "";
    public string Mode { get; set; } = "Single";
    [JsonConverter(typeof(JsonStringEnumConverter<RiskLossBasis>))]
    public RiskLossBasis LossBasis { get; set; } = RiskLossBasis.RealizedOnly;
    public decimal TradedCapital { get; set; } = 50_000m;
    public int? StrategyId { get; set; }
    public int? TunedId { get; set; }
}

internal static class JobFileJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true
    };
}
