using AlgoTrading.Models;
using Core = Stage1Scanner.Core.Market;
using Rules = Stage1Scanner.Core.Rules;

namespace AlgoTrading.Backtest.Runtime;

public sealed partial class IndicatorProvider
{
    private IndicatorSeries ComputeCount(IndicatorExpression expr, Timeframe tf)
    {
        if (expr.Lookback <= 0 || expr.Predicate is null || !expr.Predicate.Enabled || expr.Predicate.Left is null || expr.Predicate.Right is null)
            throw new ArgumentException("Count requires a positive lookback and an enabled predicate.");
            
        var aligned = _accessor.Resolve(tf);
        var candles = ToCandles(aligned, tf);
        
        // Note: _references is now IReadOnlyDictionary<string, ReferenceValue>
        var expressions = new ExpressionEvaluator(_accessor.ForTimeframe(tf), this, _parameters, _references);
        var conditions = new ConditionEvaluator(expressions);
        
        var truth = new Dictionary<DateTimeOffset, Rules.RuleTruth>();
        for (int i = 0; i < candles.Count; i++)
        {
            truth[candles[i].CloseTime] = conditions.TryEvaluateCondition(expr.Predicate, tf, i, out var match)
                ? match ? Rules.RuleTruth.True : Rules.RuleTruth.False
                : Rules.RuleTruth.Unknown;
        }
        
        var instrument = new Core.Instruments(_accessor.BaseSeries.InstrumentToken, _accessor.BaseSeries.Symbol);
        var rule = Rules.AggregateRule.Count(new CountPredicate(truth), candles.Timeframe,
            Rules.ComparisonOperator.GreaterOrEqual, 0, expr.Lookback, expr.Streak);
            
        var values = new decimal?[candles.Count];
        for (int i = expr.Lookback - 1; i < candles.Count; i++)
        {
            instrument.SetCandles(candles.Timeframe,
                Enumerable.Range(i - expr.Lookback + 1, expr.Lookback).Select(j => candles[j]));
            var result = rule.Evaluate(new Rules.EvaluationContext(instrument, candles[i].CloseTime));
            values[i] = result.Truth == Rules.RuleTruth.Unknown ? null : result.Left;
        }
        return Result(["Value"], values);
    }

    private sealed class CountPredicate(IReadOnlyDictionary<DateTimeOffset, Rules.RuleTruth> truth) : Rules.IRuleExpression
    {
        public Rules.RuleEvaluation Evaluate(Rules.EvaluationContext context)
            => new(truth.GetValueOrDefault(context.EvaluationTime, Rules.RuleTruth.Unknown), "COUNT_PREDICATE");
    }
}