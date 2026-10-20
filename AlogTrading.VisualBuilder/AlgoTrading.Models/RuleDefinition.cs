namespace AlgoTrading.Models;

public sealed class RuleDefinition
{
    public int SchemaVersion { get; set; } = 1;
    public string StrategyName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public uint? InstrumentToken { get; set; }
    public string? InstrumentDisplay { get; set; }
    public Timeframe? TargetTimeframe { get; set; }
    public Direction Direction { get; set; } = Direction.Long;
    public Timeframe EvaluationTimeframe { get; set; }
    public ExecutionMode ExecutionMode { get; set; } = ExecutionMode.Delivery;
    public Timeframe BaseTimeframe { get; set; }
    public List<ParameterSpec> Parameters { get; set; } = new();
    public ConditionGroup? Eligibility { get; set; }
    public ConditionGroup Entry { get; set; } = new();
    public ConditionGroup? Protection { get; set; }
    public ConditionGroup? Exit { get; set; }
    public ConditionGroup? FullExit { get; set; }
    public StopLossSpec? StopLoss { get; set; }
    public TargetSpec? Target { get; set; }
}