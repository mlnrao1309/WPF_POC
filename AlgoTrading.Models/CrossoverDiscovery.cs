namespace AlgoTrading.Models.Rules
{
    /// <summary>Finds enabled crossover clauses without evaluating historical Count windows or indicator caches.</summary>
    internal sealed class CrossoverDiscovery
    {
        private readonly List<CrossoverSubscription> subscriptions = new List<CrossoverSubscription>();
        internal IReadOnlyList<CrossoverSubscription> Discover(RuleDefinition definition)
        {
            subscriptions.Clear();
            VisitCondition(definition.Condition, definition.EvaluationTimeframe, "condition");
            return subscriptions.AsReadOnly();
        }

        private void VisitCondition(ConditionDefinition condition, string timeframe, string path)
        {
            if (!condition.Enabled)
            {
                return;
            }

            switch (condition)
            {
                case ComparisonCondition comparison:
                    if (comparison.Operator is ComparisonOperator.CrossedAbove or ComparisonOperator.CrossedBelow)
                    {
                        subscriptions.Add(new CrossoverSubscription(path, timeframe, comparison));
                    }

                    VisitValue(comparison.Left, timeframe, path + ".left");
                    VisitValue(comparison.Right, timeframe, path + ".right");
                    break;
                case ConditionGroup group:
                    for (int conditionIndex = 0; conditionIndex < group.Conditions.Count; conditionIndex++)
                    {
                        VisitCondition(group.Conditions[conditionIndex], timeframe, path + ".conditions[" + conditionIndex + "]");
                    }

                    break;
                case NotCondition negation:
                    VisitCondition(negation.Condition, timeframe, path + ".not");
                    break;
            }
        }

        private void VisitValue(ValueDefinition value, string timeframe, string path)
        {
            switch (value)
            {
                case IndicatorValue indicator when indicator.Source != null:
                    VisitValue(indicator.Source, indicator.Timeframe, path + ".source");
                    break;
                case ArithmeticValue arithmetic:
                    VisitValue(arithmetic.Left, timeframe, path + ".left");
                    VisitValue(arithmetic.Right, timeframe, path + ".right");
                    break;
                case RoundValue round:
                    VisitValue(round.Source, timeframe, path + ".source");
                    break;
                case PreviousValue previous:
                    VisitValue(previous.Source, previous.Timeframe, path + ".source");
                    break;
                case CountValue count:
                    VisitCondition(count.Condition, count.Timeframe, path + ".countedCondition");
                    break;
            }
        }
    }
}

