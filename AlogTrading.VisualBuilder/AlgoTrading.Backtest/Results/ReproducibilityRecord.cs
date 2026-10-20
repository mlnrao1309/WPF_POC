namespace AlgoTrading.Backtest.Results;

public sealed class ReproducibilityRecord
{
    public string? RuleJson { get; init; }
    public string? ParametersJson { get; init; }
    public DateTime DataFrom { get; init; }
    public DateTime DataTo { get; init; }
    public string? CostConfigJson { get; init; }
    public string? SlippageConfigJson { get; init; }
    public string? MarginConfigJson { get; init; }
    public string EngineVersion { get; init; } = "";
    public DateTime RunAt { get; init; }
    public string DataSource { get; init; } = "";
    public uint InstrumentToken { get; init; }
    public string BaseTimeframe { get; init; } = "";
    public Guid? RunId { get; init; }
}