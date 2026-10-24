using AlgoTrading.Backtest.Runtime;
using AlgoTrading.Models;
using AlgoTrading.Backtest.Results;
using Xunit;

namespace AlgoTrading.Backtest.Tests;

public class EquityCurveTests
{
    [Fact]
    public void PreparedSeries_ResolveFinerCandlesWithoutDownwardAggregation()
    {
        var hour=EngineTestSeries.FromOhlc(Timeframe.Parse("1H"),Start,(100,100,100,100),(100,100,100,100),(100,100,100,100));
        var quarter=EngineTestSeries.FromOhlc(Timeframe.Parse("15m"),Start,Enumerable.Repeat((100m,100m,100m,100m),12).ToArray());
        var rule=RuleBuilder.AlwaysEntryLong();rule.EvaluationTimeframe=rule.BaseTimeframe=Timeframe.Parse("1H");
        ((FieldExpression)((Condition)rule.Entry.Children[0]).Left).Timeframe=Timeframe.Parse("15m");
        var result=new BacktestEngine(new BacktestEngineOptions
        {
            InitialCapital=50000,CostsJson=TestJson.ZeroCosts,SlippageJson=TestJson.ZeroSlippage,
            PreparedSeries=new Dictionary<Timeframe,AlgoTrading.Data.IBarSeries>{{Timeframe.Parse("15m"),quarter}}
        }).Run(rule,hour);
        Assert.Single(result.Trades);
    }

    [Fact]
    public void CandleAlignment_DoesNotTreatNextSessionsOpenAsPreviousCandlesClose()
    {
        var source=EngineTestSeries.FromOhlc(Timeframe.Parse("1H"),Start,(100,100,100,100),(100,100,100,100));
        var hour=new EngineTestSeries{Timeframe=source.Timeframe,Open=source.Open,High=source.High,Low=source.Low,Close=source.Close,
            Volume=source.Volume,OI=source.OI,DateTime=new[]{Start,Start.AddDays(1)}};
        var quarter=EngineTestSeries.FromOhlc(Timeframe.Parse("15m"),Start,Enumerable.Repeat((100m,100m,100m,100m),8).ToArray());
        var accessor=new TimeframeAccessor(1,hour,new AlgoTrading.Aggregation.TimeframeCache(hour,preparedSeries:
            new Dictionary<Timeframe,AlgoTrading.Data.IBarSeries>{{quarter.Timeframe,quarter}}));
        Assert.Equal(3,accessor.Resolve(quarter.Timeframe).BaseToTfIndex[0]);
    }

    [Theory]
    [InlineData(Direction.Long, 50000, false)]
    [InlineData(Direction.Short, 50000, false)]
    [InlineData(Direction.Long, 50020, true)]
    [InlineData(Direction.Short, 50020, true)]
    public void Entry_RequiresCapitalForMarginAndEntryCharges(Direction direction, int initialCapital, bool canEnter)
    {
        var bars=EngineTestSeries.FromOhlc(Timeframe.Parse("D"),Start,
            (50000,50000,50000,50000),(50000,50000,50000,50000),
            (50000,50000,50000,50000),(50000,50000,50000,50000));
        var rule=RuleBuilder.AlwaysEntryLong();rule.Direction=direction;
        var result=new BacktestEngine(new BacktestEngineOptions
        {
            InitialCapital=initialCapital,
            CostsJson=TestJson.ZeroCosts.Replace("\"brokeragePerOrder\": 0", "\"brokeragePerOrder\": 20"),
            SlippageJson=TestJson.ZeroSlippage,MarginsJson=TestJson.Delivery100
        }).Run(rule,bars);
        Assert.Equal(canEnter ? 1 : 0,result.Trades.Count);
        if(canEnter)
        {
            Assert.Equal(initialCapital-20m,result.EquityCurve[2].Equity);
            Assert.Equal(initialCapital-40m,result.EquityCurve[^1].Equity);
        }
        else Assert.All(result.EquityCurve,p=>Assert.Equal(initialCapital,p.Equity));
    }

    [Theory]
    [InlineData(Direction.Long, RiskLossBasis.RealizedOnly, true)]
    [InlineData(Direction.Long, RiskLossBasis.IncludeUnrealized, false)]
    [InlineData(Direction.Short, RiskLossBasis.RealizedOnly, true)]
    [InlineData(Direction.Short, RiskLossBasis.IncludeUnrealized, false)]
    public void RiskAssessment_OpenLossRecovers_ChoiceDoesNotChangeTrades(Direction direction, RiskLossBasis basis, bool approved)
    {
        decimal adverse = direction == Direction.Long ? 9000m : 11000m;
        var bars = EngineTestSeries.FromOhlc(Timeframe.Parse("1m"), Start,
            (10000,10000,10000,10000), (10000,10000,10000,10000),
            (10000,Math.Max(10000,adverse),Math.Min(10000,adverse),adverse), (10000,10000,10000,10000));
        var rule = RuleBuilder.AlwaysEntryLong();
        rule.Direction = direction;
        rule.EvaluationTimeframe = rule.BaseTimeframe = Timeframe.Parse("1m");
        var options = new BacktestEngineOptions
        {
            InitialCapital = 50000, LossBasis = basis,
            CostsJson = TestJson.ZeroCosts, SlippageJson = TestJson.ZeroSlippage, MarginsJson = TestJson.Delivery100
        };
        var result = new BacktestEngine(options).Run(rule,bars);
        var trade = Assert.Single(result.Trades);
        Assert.Equal(0m,trade.NetPnL);
        Assert.Equal(ExitReason.EndOfData,trade.ExitReason);
        Assert.Equal(bars.DateTime.Span[3],trade.ExitTime);
        Assert.Equal(approved,result.RiskAssessment.IsBacktestApproved);
        Assert.Equal(1000m,result.RiskAssessment.DailyLossLimit);
        Assert.Equal(2500m,result.RiskAssessment.MonthlyLossLimit);
        if (!approved)
        {
            var breach = Assert.Single(result.RiskAssessment.Breaches);
            Assert.Equal(RiskPeriod.Daily,breach.Period);
            Assert.Equal(1000m,breach.Loss);
            Assert.Equal(bars.DateTime.Span[2],breach.AtUtc);
        }
        var restored = BacktestResultJson.Deserialize(BacktestResultJson.Serialize(result))!;
        Assert.Equal(basis,restored.RiskAssessment.LossBasis);
        Assert.Equal(approved,restored.RiskAssessment.IsBacktestApproved);
    }

    [Fact]
    public void RiskAssessment_MonthlyAccumulationAndIstReset_PreserveBoundaryPnL()
    {
        // 18:30 UTC is midnight IST: losses belong to the new local day/month.
        var start = new DateTime(2026,1,24,18,30,0,DateTimeKind.Utc);
        var points = new List<EquityPoint>{new(){DateTime=start,Equity=50000}};
        var trades = new List<Trade>();
        for(int i=1;i<=7;i++)
        {
            decimal pnl=i==7 ? 1000m : -500m;
            var at=start.AddDays(i);
            trades.Add(new Trade{ExitTime=at,NetPnL=pnl});
            points.Add(new EquityPoint{DateTime=at,Equity=points[^1].Equity+pnl});
        }
        var risk=RiskAssessmentCalculator.Compute(new BacktestResult{Trades=trades,EquityCurve=points},new BacktestEngineOptions{InitialCapital=50000});
        Assert.False(risk.IsBacktestApproved);
        var breach=Assert.Single(risk.Breaches);
        Assert.Equal(RiskPeriod.Monthly,breach.Period);
        Assert.Equal(2500m,breach.Loss);
        var months=risk.Periods.Where(p=>p.Period==RiskPeriod.Monthly).ToArray();
        Assert.Equal(2,months.Length);
        Assert.Equal(-3000m,months[0].RealizedPnL);
        Assert.Equal(1000m,months[1].RealizedPnL);
        Assert.Equal(47000m,months[1].OpeningRealizedCapital);
        Assert.Equal(-2000m,months.Sum(p=>p.RealizedPnL));
        Assert.Null(months[1].FirstBreachAtUtc);
    }

    [Fact]
    public void RiskAssessment_RealizedBreachSurvivesSameBarRecovery()
    {
        var at=Start.AddMinutes(1);
        var risk=RiskAssessmentCalculator.Compute(new BacktestResult
        {
            Trades=new[]{new Trade{ExitTime=at,NetPnL=-1000},new Trade{ExitTime=at,NetPnL=1000}},
            EquityCurve=new[]{new EquityPoint{DateTime=Start,Equity=50000},new EquityPoint{DateTime=at,Equity=50000}}
        },new BacktestEngineOptions{InitialCapital=50000});
        Assert.False(risk.IsBacktestApproved);
        Assert.Equal(0m,risk.Periods.Single(p=>p.Period==RiskPeriod.Monthly).RealizedPnL);
    }

    [Fact]
    public void Capital_IsNotCountedTwice_WhenTestingMargin()
    {
        var bars=EngineTestSeries.FromOhlc(Timeframe.Parse("D"),Start,
            (75000,75000,75000,75000),(75000,75000,75000,75000),(75000,75000,75000,75000));
        var result=new BacktestEngine(new BacktestEngineOptions
        {
            InitialCapital=50000,CostsJson=TestJson.ZeroCosts,SlippageJson=TestJson.ZeroSlippage,MarginsJson=TestJson.Delivery100
        }).Run(RuleBuilder.AlwaysEntryLong(),bars);
        Assert.Empty(result.Trades);
        Assert.Null(result.RiskAssessment.IsBacktestApproved);
        Assert.Equal(RiskAssessmentReason.NoClosedTrades,result.RiskAssessment.Reason);
    }

    [Theory]
    [InlineData(Direction.Long)]
    [InlineData(Direction.Short)]
    public void OpenEquity_RecognizesEntryCharges_WithoutDoubleChargingClosedTrade(Direction direction)
    {
        var bars=EngineTestSeries.FromOhlc(Timeframe.Parse("D"),Start,
            (100,100,100,100),(100,100,100,100),(100,100,100,100),(100,100,100,100));
        var rule=RuleBuilder.AlwaysEntryLong();rule.Direction=direction;
        var result=new BacktestEngine(new BacktestEngineOptions
        {
            InitialCapital=50000,CostsJson=TestJson.ZeroCosts.Replace("\"brokeragePerOrder\": 0", "\"brokeragePerOrder\": 20"),
            SlippageJson=TestJson.ZeroSlippage,MarginsJson=TestJson.Delivery100
        }).Run(rule,bars);
        Assert.Equal(49980m,result.EquityCurve[2].Equity);
        Assert.Equal(-40m,Assert.Single(result.Trades).NetPnL);
        Assert.Equal(50000+Assert.Single(result.Trades).NetPnL,result.EquityCurve[^1].Equity);
        Assert.Equal(50000m,result.RiskAssessment.CapitalCurve[2].RealizedCapital);
    }

    [Fact]
    public void EmptyData_IsUnassessedWithEffectiveConfiguration()
    {
        var result=new BacktestEngine(new BacktestEngineOptions{InitialCapital=50000,LossBasis=RiskLossBasis.IncludeUnrealized})
            .Run(RuleBuilder.AlwaysEntryLong(),EngineTestSeries.FromOhlc(Timeframe.Parse("D"),Start,(100,100,100,100)));
        Assert.Null(result.RiskAssessment.IsBacktestApproved);
        Assert.Equal(50000m,result.RiskAssessment.TradedCapital);
        Assert.Equal(RiskLossBasis.IncludeUnrealized,result.RiskAssessment.LossBasis);
    }

    private static readonly DateTime Start = new(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void EquityCurve_Length_Equals_BarCount()
    {
        var series = EngineTestSeries.FromOhlc(Timeframe.Parse("D"), Start,
            (100m, 101m, 99m, 100m),
            (101m, 102m, 100m, 101m),
            (102m, 103m, 101m, 102m),
            (103m, 104m, 102m, 103m),
            (104m, 105m, 103m, 104m));

        var engine = new BacktestEngine(RuleBuilder.ZeroCostOptions());
        var result = engine.Run(RuleBuilder.AlwaysEntryLong(), series);

        Assert.Equal(series.Count, result.EquityCurve.Count);
    }

    [Fact]
    public void FirstEquityPoint_IsInitialCapital()
    {
        var series = EngineTestSeries.FromOhlc(Timeframe.Parse("D"), Start,
            (100m, 101m, 99m, 100m),
            (101m, 102m, 100m, 101m));

        var engine = new BacktestEngine(RuleBuilder.ZeroCostOptions());
        var result = engine.Run(RuleBuilder.AlwaysEntryLong(), series);

        Assert.Equal(100_000m, result.EquityCurve[0].Equity);
    }

    [Fact]
    public void Equity_Marks_OpenPosition_To_Market()
    {
        // bar 2: entry at 102, close at 103 â†’ unrealized +1
        var series = EngineTestSeries.FromOhlc(Timeframe.Parse("D"), Start,
            (100m, 101m, 99m, 100m),
            (101m, 102m, 100m, 101m),
            (102m, 103m, 101m, 103m),
            (103m, 104m, 102m, 104m));

        var engine = new BacktestEngine(RuleBuilder.ZeroCostOptions(quantity: 1));
        var result = engine.Run(RuleBuilder.AlwaysEntryLong(), series);

        // bar 2 close = 103, entry at 102 â†’ unrealized = +1
        Assert.Equal(100_001m, result.EquityCurve[2].Equity);
    }

    [Fact]
    public void FinalEquity_Equals_Initial_Plus_Realized()
    {
        var series = EngineTestSeries.FromOhlc(Timeframe.Parse("D"), Start,
            (100m, 101m, 99m, 100m),
            (101m, 102m, 100m, 101m),
            (102m, 103m, 101m, 102m),
            (103m, 104m, 102m, 104m));

        var engine = new BacktestEngine(RuleBuilder.ZeroCostOptions(quantity: 1));
        var result = engine.Run(RuleBuilder.AlwaysEntryLong(), series);

        // entry at 102, force close at last close 104 â†’ +2
        Assert.Equal(100_002m, result.EquityCurve[^1].Equity);
    }
}
