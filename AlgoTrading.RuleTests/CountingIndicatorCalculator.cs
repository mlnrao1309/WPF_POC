using AlgoTrading.Models;
using AlgoTrading.Models.Rules;

internal sealed class CountingIndicatorCalculator : IIndicatorSeriesCalculator
{
    private int calculationCount;

    internal int CalculationCount
    {
        get
        {
            return this.calculationCount;
        }
    }

    public double[] Calculate(IndicatorDependency dependency, RuleMarketData data)
    {
        this.calculationCount++;
        double[] values = new double[3];
        values[0] = dependency.Length;
        values[1] = dependency.Multiplier;
        values[2] = data.InstrumentToken;
        return values;
    }
}
