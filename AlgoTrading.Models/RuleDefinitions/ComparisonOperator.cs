using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AlgoTrading.Models.Rules
{
    public enum ComparisonOperator
    {
        GreaterThan,
        GreaterThanOrEqual,
        LessThan,
        LessThanOrEqual,
        Equal,
        NotEqual,
        CrossedAbove,
        CrossedBelow
    }
}


