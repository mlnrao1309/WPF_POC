using AlgoTrading.Models.Rules;

internal static class RuleModelCompatibilityData
{
    internal static ComparisonCondition DisabledComparison()
    {
        return new ComparisonCondition(new NumberValue(1), ComparisonOperator.Equal, new NumberValue(2), false);
    }

    internal static ComparisonCondition DisabledComparison(ComparisonCondition source)
    {
        return new ComparisonCondition(source.Left, source.Operator, source.Right, false, source.Comment);
    }
}
