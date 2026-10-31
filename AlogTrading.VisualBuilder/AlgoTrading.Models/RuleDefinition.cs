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

    // NEW: Array of Reference Blocks
    public List<ReferenceBlock> References { get; set; } = new();

    public ConditionGroup? Eligibility { get; set; }
    public ConditionGroup Entry { get; set; } = new();
    public ConditionGroup? Protection { get; set; }
    public ConditionGroup? Exit { get; set; }
    public ConditionGroup? FullExit { get; set; }
    public StopLossSpec? StopLoss { get; set; }
    public TargetSpec? Target { get; set; }
}

public sealed class ReferenceBlock
{
    public string Name { get; set; } = string.Empty; // Optional, for UI readability
    public Timeframe Timeframe { get; set; }
    public Node Trigger { get; set; } = null!;
    public Node? ClearOn { get; set; }
    public List<ReferenceCapture> Captures { get; set; } = new();
}

public sealed class ReferenceCapture
{
    public string Id { get; set; } = string.Empty; // The variable name
    public ValueExpression Value { get; set; } = null!; // What to calculate and store
}