using System;
using System.Collections.Generic;
using System.Globalization;

namespace AlgoTrading.Models.Rules
{
    public static class RuleBinder
    {
        public static BoundRule Bind(RuleDefinition definition)
        {
            RuleBindingContext context = new RuleBindingContext();
            return context.Bind(definition);
        }
    }
}
