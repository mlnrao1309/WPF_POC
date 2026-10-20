namespace AlgoTrading.Models;

public sealed class StopLossSpec
{
    public StopLossMode Mode { get; set; }
    public decimal? Value { get; set; }
    public string? GroupName { get; set; }
}