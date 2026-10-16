namespace AlgoTrading.Models
{
    public interface IDailyPivotFormula
    {
        DailyPivotNumbers Calculate(decimal high, decimal low, decimal close, decimal openQ, decimal closeQ);
    }
}
