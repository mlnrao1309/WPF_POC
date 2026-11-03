namespace AlgoTrading.Models;

public sealed class SweepBinding
{
    /// <summary>The display name of the binding (e.g., "EmaPeriod").</summary>
    public required string Name { get; init; }
    
    /// <summary>The RFC 6901 JSON pointer (e.g., "/entry/children/0/params/period"). Null if targeting a ParameterSpec.</summary>
    public string? JsonPointer { get; init; }
    
    /// <summary>The list of values to test for this binding.</summary>
    public required List<decimal> Values { get; init; }
}