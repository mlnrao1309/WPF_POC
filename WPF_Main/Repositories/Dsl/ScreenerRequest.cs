// Dsl/ScreenerRequest.cs
namespace AlgoTrading.Screener.Dsl;

public sealed class ScreenerRequest
{
    public required string TimeFrame { get; init; }
    public DateTime FromInclusive { get; init; }
    public DateTime ToExclusive   { get; init; }
    public string? Segment { get; init; }
    public string? Exchange { get; init; }
}