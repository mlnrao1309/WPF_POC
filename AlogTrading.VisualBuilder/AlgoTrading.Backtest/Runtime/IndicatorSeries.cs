namespace AlgoTrading.Backtest.Runtime;

/// <summary>
/// Cached output columns for one indicator evaluated on one timeframe.
/// Bar indices align with the timeframe's own bar series (not base bars).
/// Outputs are nullable; null means "no value" (warmup or undefined).
/// </summary>
public sealed class IndicatorSeries
{
    public required IReadOnlyList<string> OutputNames { get; init; }
    public required decimal?[][] Outputs { get; init; }
    public required int FirstValidIndex { get; init; }

    public int OutputCount => Outputs.Length;

    public int IndexOfOutput(string name)
    {
        for (int i = 0; i < OutputNames.Count; i++)
            if (string.Equals(OutputNames[i], name, StringComparison.Ordinal)) return i;
        return -1;
    }
}