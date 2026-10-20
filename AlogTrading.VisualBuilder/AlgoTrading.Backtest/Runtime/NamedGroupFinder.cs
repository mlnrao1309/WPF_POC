using AlgoTrading.Models;

namespace AlgoTrading.Backtest.Runtime;

internal static class NamedGroupFinder
{
    public static ConditionGroup? Find(RuleDefinition rule, string name)
    {
        if (string.IsNullOrEmpty(name)) return null;

        foreach (var root in new[] { rule.Eligibility, rule.Entry, rule.Protection, rule.Exit, rule.FullExit })
        {
            var hit = FindIn(root, name);
            if (hit is not null) return hit;
        }
        return null;
    }

    private static ConditionGroup? FindIn(ConditionGroup? group, string name)
    {
        if (group is null) return null;
        if (string.Equals(group.Name, name, StringComparison.Ordinal)) return group;
        foreach (var child in group.Children)
        {
            if (child is ConditionGroup g)
            {
                var hit = FindIn(g, name);
                if (hit is not null) return hit;
            }
        }
        return null;
    }
}