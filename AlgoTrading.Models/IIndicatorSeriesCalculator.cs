using System;
using System.Collections.Generic;
using AlgoTrading.Models.Rules;

namespace AlgoTrading.Models
{
    public interface IIndicatorSeriesCalculator
    {
        double[] Calculate(IndicatorDependency dependency, RuleMarketData data);
    }
}
