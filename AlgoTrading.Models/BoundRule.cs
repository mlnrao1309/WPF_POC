using System;
using System.Collections.Generic;
using System.Globalization;

namespace AlgoTrading.Models.Rules
{
    public sealed class BoundRule
    {
        private readonly RuleDefinition definition;
        private readonly RuleBindingPlan plan;

        internal BoundRule(RuleDefinition definition, RuleBindingPlan plan)
        {
            this.definition = definition;
            this.plan = plan;
        }

        public RuleBindingPlan Plan
        {
            get
            {
                return plan;
            }
        }

        public string Description
        {
            get
            {
                return RuleLanguage.Describe(definition.Condition);
            }
        }

        public RuleCrossoverMonitor CreateCrossoverMonitor(DateTimeOffset? startAfter = null)
        {
            return CreateCrossoverMonitor(IndicatorCalculationVersion.LegacyV1, "monitor-snapshot", new IndicatorCalculationCache(), startAfter);
        }

        public RuleCrossoverMonitor CreateCrossoverMonitor(IndicatorCalculationVersion version, string dataRevision,
            IndicatorCalculationCache cache, DateTimeOffset? startAfter = null)
        {
            ArgumentNullException.ThrowIfNull(cache);
            ArgumentException.ThrowIfNullOrWhiteSpace(dataRevision);
            if (!Enum.IsDefined(version))
            {
                throw new ArgumentOutOfRangeException(nameof(version));
            }
            return new RuleCrossoverMonitor(this, definition, startAfter, version, dataRevision, cache);
        }

        public BoundRuleExecution BindData(RuleMarketData data)
        {
            return BindData(data, new IndicatorCalculationCache(), "legacy-snapshot", IndicatorCalculationVersion.LegacyV1);
        }

        public BoundRuleExecution BindData(RuleMarketData data, IndicatorCalculationCache cache,
            string dataRevision, IndicatorCalculationVersion version)
        {
            ArgumentNullException.ThrowIfNull(data);
            ArgumentNullException.ThrowIfNull(cache);
            ArgumentException.ThrowIfNullOrWhiteSpace(dataRevision);
            if (!Enum.IsDefined(version))
            {
                throw new ArgumentOutOfRangeException(nameof(version));
            }
            foreach (string timeframe in Plan.RequiredTimeframes)
            {
                if (!data.Contains(timeframe))
                {
                    throw new ArgumentException($"Supply completed candles for '{timeframe}'.", nameof(data));
                }
            }
            return new BoundRuleExecution(definition, data, cache, dataRevision, version);
        }
    }
}
