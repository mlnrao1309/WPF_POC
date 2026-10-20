namespace AlgoTrading.Models;

public sealed class ParameterSpec
{
    public string Name { get; set; } = string.Empty;
    public decimal Default { get; set; }
    public decimal Minimum { get; set; }
    public decimal Maximum { get; set; }
    public decimal Step { get; set; } = 1m;
}