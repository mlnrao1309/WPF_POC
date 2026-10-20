using System;
using System.Collections.Generic;
using AlgoTrading.Models;

namespace AlgoTrading.Backtest;

internal sealed class NamedGroupIndex
{
    private readonly Dictionary<string, ConditionGroup> _map = new(StringComparer.Ordinal);

    public void Index(RuleDefinition r)
    {
        _map.Clear();
        Add(r.Eligibility); Add(r.Entry); Add(r.Protection); Add(r.Exit); Add(r.FullExit);
    }

    public ConditionGroup Get(string name)
    {
        if (!_map.TryGetValue(name, out var g)) throw new InvalidOperationException($"Named group '{name}' not found.");
        return g;
    }

    private void Add(ConditionGroup? g)
    {
        if (g is null) return;
        if (!string.IsNullOrEmpty(g.Name))
        {
            if (_map.ContainsKey(g.Name)) throw new InvalidOperationException($"Duplicate named group '{g.Name}'.");
            _map[g.Name] = g;
        }
        if (g.Children is null) return;
        var ch = (IReadOnlyList<Node>)g.Children;
        for (int i = 0; i < ch.Count; i++)
            if (ch[i] is ConditionGroup cg) Add(cg);
    }
}