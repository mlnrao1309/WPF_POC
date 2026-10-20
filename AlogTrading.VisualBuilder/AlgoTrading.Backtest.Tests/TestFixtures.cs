using AlgoTrading.Data;
using AlgoTrading.Models;

namespace AlgoTrading.Backtest.Tests;

internal sealed class EngineTestSeries : IBarSeries
{
    public uint InstrumentToken { get; init; } = 1;
    public string Symbol { get; init; } = "TEST";
    public Timeframe Timeframe { get; init; }
    public DateTime From => DateTime.Span.Length > 0 ? DateTime.Span[0] : default;
    public DateTime To => DateTime.Span.Length > 0 ? DateTime.Span[^1] : default;
    public int Count => Open.Length;

    public ReadOnlyMemory<decimal> Open { get; init; }
    public ReadOnlyMemory<decimal> High { get; init; }
    public ReadOnlyMemory<decimal> Low { get; init; }
    public ReadOnlyMemory<decimal> Close { get; init; }
    public ReadOnlyMemory<decimal> Volume { get; init; }
    public ReadOnlyMemory<decimal> OI { get; init; }
    public ReadOnlyMemory<DateTime> DateTime { get; init; }

    public static EngineTestSeries FromOhlc(
        Timeframe tf, DateTime start, params (decimal O, decimal H, decimal L, decimal C)[] bars)
    {
        int n = bars.Length;
        var open = new decimal[n];
        var high = new decimal[n];
        var low = new decimal[n];
        var close = new decimal[n];
        var vol = new decimal[n];
        var oi = new decimal[n];
        var dt = new DateTime[n];

        TimeSpan step = tf.Unit switch
        {
            TimeframeUnit.m => TimeSpan.FromMinutes(tf.ToMinutes()),
            TimeframeUnit.H => TimeSpan.FromMinutes(tf.ToMinutes()),
            _ => TimeSpan.FromDays(1)
        };

        for (int i = 0; i < n; i++)
        {
            open[i] = bars[i].O;
            high[i] = bars[i].H;
            low[i] = bars[i].L;
            close[i] = bars[i].C;
            vol[i] = 1000m;
            oi[i] = 0m;
            dt[i] = start.Add(step * i);
        }

        return new EngineTestSeries
        {
            Timeframe = tf,
            Open = open, High = high, Low = low, Close = close,
            Volume = vol, OI = oi, DateTime = dt
        };
    }
}

internal static class TestJson
{
    public const string ZeroCosts = """
    {
      "schemaVersion": 1,
      "equity": {
        "delivery": {
          "brokeragePerOrder": 0, "sttBuyPct": 0, "sttSellPct": 0,
          "exchangeTxnPct": 0, "sebiRupeesPerCrore": 0,
          "stampDutyBuyPct": 0, "gstPct": 0
        },
        "intraday": {
          "brokeragePerOrder": 0, "sttBuyPct": 0, "sttSellPct": 0,
          "exchangeTxnPct": 0, "sebiRupeesPerCrore": 0,
          "stampDutyBuyPct": 0, "gstPct": 0
        }
      }
    }
    """;

    public const string ZeroSlippage = """
    { "schemaVersion": 1,
      "equity": { "model": "Spread", "spreadPercent": 0, "fixedAmount": 0 } }
    """;

    public const string Delivery100 = """
    { "schemaVersion": 1,
      "equity": {
        "delivery": { "marginPercent": 100 },
        "intraday": { "marginPercent": 20 } } }
    """;
}

internal static class RuleBuilder
{
    public static Condition AlwaysTrue()
    {
        return new Condition
        {
            Enabled = true,
            Left = new FieldExpression { Field = Field.Close, Offset = 0 },
            Operator = Operator.GreaterThan,
            Right = new NumberExpression { Value = 0m }
        };
    }

    public static Condition CloseGreaterThan(decimal threshold, int offset = 0)
    {
        return new Condition
        {
            Enabled = true,
            Left = new FieldExpression { Field = Field.Close, Offset = offset },
            Operator = Operator.GreaterThan,
            Right = new NumberExpression { Value = threshold }
        };
    }

    public static ConditionGroup AllOf(params Node[] children)
    {
        return new ConditionGroup
        {
            Enabled = true,
            Logic = GroupLogic.All,
            Children = new List<Node>(children)
        };
    }

    public static RuleDefinition AlwaysEntryLong(
        StopLossSpec? sl = null, TargetSpec? tp = null,
        ConditionGroup? fullExit = null)
    {
        return new RuleDefinition
        {
            SchemaVersion = 1,
            StrategyName = "Test",
            Direction = Direction.Long,
            EvaluationTimeframe = Timeframe.Parse("D"),
            ExecutionMode = ExecutionMode.Delivery,
            BaseTimeframe = Timeframe.Parse("D"),
            Entry = AllOf(AlwaysTrue()),
            StopLoss = sl,
            Target = tp,
            FullExit = fullExit
        };
    }

    public static BacktestEngineOptions ZeroCostOptions(int quantity = 1) => new()
    {
        InitialCapital = 100_000m,
        Quantity = quantity,
        CostsJson = TestJson.ZeroCosts,
        SlippageJson = TestJson.ZeroSlippage,
        MarginsJson = TestJson.Delivery100
    };
}