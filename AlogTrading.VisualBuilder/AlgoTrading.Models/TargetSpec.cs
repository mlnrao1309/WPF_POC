namespace AlgoTrading.Models;

public sealed class TargetSpec
{
    public TargetMode Mode { get; set; }
    public decimal? Value { get; set; }
    public string? GroupName { get; set; }
}