using System;

namespace AlgoTrading.Models
{
    /// <summary>Reference for SQL decimal(18,2) inputs; the supplied SQL division yields decimal(25,6).</summary>
    public sealed class SqlRangeExtensionPivotCalculator : IDailyPivotFormula
    {
        public DailyPivotNumbers Calculate(decimal high, decimal low, decimal close, decimal openQ, decimal closeQ)
        {
            ValidateSourcePrecision(openQ);
            ValidateSourcePrecision(closeQ);
            SqlRangeExtensionPivots pivots = Calculate(high, low, close);
            bool ppr1 = IsBetween(openQ, pivots.Pivot, pivots.Resistance1) && IsBetween(closeQ, pivots.Pivot, pivots.Resistance1);
            bool pps1 = IsBetween(openQ, pivots.Support1, pivots.Pivot) && IsBetween(closeQ, pivots.Support1, pivots.Pivot);
            return new DailyPivotNumbers(pivots, ppr1, pps1);
        }

        private bool IsBetween(decimal value, decimal first, decimal second)
        {
            return value >= Math.Min(first, second) && value <= Math.Max(first, second);
        }

        public SqlRangeExtensionPivots Calculate(decimal high, decimal low, decimal close)
        {
            if (high < low || close < low || close > high)
            {
                throw new ArgumentException("Source OHLC bounds are invalid.");
            }
            ValidateSourcePrecision(high);
            ValidateSourcePrecision(low);
            ValidateSourcePrecision(close);
            decimal pivot = Math.Round((high + low + close) / 3m, 6, MidpointRounding.AwayFromZero);
            decimal range = high - low;
            return new SqlRangeExtensionPivots(pivot,
                2m * pivot - low, pivot + range, pivot + 2m * range, pivot + 3m * range, pivot + 4m * range,
                2m * pivot - high, pivot - range, pivot - 2m * range, pivot - 3m * range, pivot - 4m * range);
        }

        private void ValidateSourcePrecision(decimal value)
        {
            if (Math.Round(value, 2) != value || Math.Abs(value) > 9999999999999999.99m)
            {
                throw new ArgumentException("SqlRangeExtensionV1 requires values representable by source decimal(18,2).");
            }
        }
    }
}
