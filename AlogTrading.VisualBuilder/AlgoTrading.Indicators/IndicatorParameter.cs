namespace AlgoTrading.Indicators;

public sealed class IndicatorParameter
{
    public string Name { get; init; } = string.Empty;
    public decimal Default { get; init; }
    public decimal Minimum { get; init; }
    public decimal Maximum { get; init; }
    public bool IntegerOnly { get; init; }
}