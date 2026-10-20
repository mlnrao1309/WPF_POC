using System.Collections.Generic;
using AlgoTrading.Models;

namespace AlgoTrading.Indicators;

public sealed class IndicatorDescriptor
{
    public IndicatorKind Kind { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public SourceKind Source { get; init; }
    public IReadOnlyList<IndicatorParameter> Parameters { get; init; } = [];
    public IReadOnlyList<string> Outputs { get; init; } = [];
    public int WarmupBarsAtDefault { get; init; }
}