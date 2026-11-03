using System.Text.Json;
using AlgoTrading.Aggregation;
using AlgoTrading.Backtest;
using AlgoTrading.Backtest.Runtime;
using AlgoTrading.Backtest.Tests;
using AlgoTrading.Models;
using AlgoTrading.Models.Analysis;
using AlgoTrading.Research;
using AlgoTrading.Research.Analysis;
using Xunit;

public class ExtendedMilestoneRequirementsTests
{
    static readonly DateTime Start=new(2025,1,1,9,0,0,DateTimeKind.Utc);
    static EngineTestSeries Bars(bool poorFuture=false) => EngineTestSeries.FromOhlc(Timeframe.Parse("15m"),Start,
        Enumerable.Range(0,160).Select(i=> {decimal p=poorFuture && i>=80 ? 200m-(i-80)*2m : 100m+i; return(p,p+1,p-1,p);}).ToArray());
    static RuleDefinition Rule(Direction d=Direction.Long) {
        var r=RuleBuilder.AlwaysEntryLong(new StopLossSpec {Mode=StopLossMode.Percentage,Value=50m});
        r.Direction=d; r.BaseTimeframe=r.EvaluationTimeframe=Timeframe.Parse("15m"); return r;
    }
    static Condition Crossing()=>new(){Left=new FieldExpression {Field=Field.Close},Operator=Operator.CrossedAbove,Right=new NumberExpression {Value=120}};
    static BacktestEngineOptions Options(int workers=1)=>new(){MaxParallelism=workers,CostsJson=TestJson.ZeroCosts,SlippageJson=TestJson.ZeroSlippage,MarginsJson=TestJson.Delivery100};
    static string Findings(RuleDefinition r)=>JsonSerializer.Serialize(RuleStaticAnalyzer.Analyze(r));
    static SweepBinding Binding(params decimal[] values)=>new(){Name="threshold",JsonPointer="/entry/children/0/right/value",Values=values.ToList()};
    static decimal Threshold(RuleDefinition r)=>((NumberExpression)((Condition)r.Entry.Children[0]).Right).Value;
    static string SweepSignature(SweepResult r)=>JsonSerializer.Serialize(r.Ranked.Select(e=>new {e.IsValid,e.Parameters,e.CriterionValue,e.Metrics.TotalNetPnL,e.Metrics.TotalTrades,e.Metrics.MaxDrawdownPct}));

    [Theory][InlineData(Direction.Long,IndicatorKind.Rsi)][InlineData(Direction.Short,IndicatorKind.Ema)]
    public void T61_T62_NoReferenceIndicatorsWorkWithoutSampleNames(Direction direction,IndicatorKind indicator) {
        var r=Rule(direction); ((Condition)r.Entry.Children[0]).Left=new IndicatorExpression {Indicator=indicator,Params=new(){["period"]=14}};
        Assert.NotNull(RuleStaticAnalyzer.Analyze(r)); Assert.Empty(r.References);
    }
    [Fact] public void T62_DisabledNestedGroupDoesNotProduceFindings() {
        var r=Rule(Direction.Short); var disabled=RuleBuilder.AllOf(Crossing(),RuleBuilder.AlwaysTrue()); disabled.Enabled=false;
        r.Entry=RuleBuilder.AllOf(disabled,RuleBuilder.AlwaysTrue()); Assert.Empty(RuleStaticAnalyzer.Analyze(r));
    }
    [Fact] public void T62_NestedAllUnderAnyProducesActualPath() {
        var r=Rule(); var nested=RuleBuilder.AllOf(Crossing(),RuleBuilder.AlwaysTrue()); r.Entry=RuleBuilder.AllOf(nested); r.Entry.Logic=GroupLogic.Any;
        Assert.Contains(RuleStaticAnalyzer.Analyze(r),f=>f.JsonPointer=="/entry/children/0/children/0" && f.Certainty==FindingCertainty.Suspected);
    }
    [Theory][InlineData(false)][InlineData(true)]
    public void T63_ExplicitMixedTimeframesAndParameterPresenceAreLegal(bool declared) {
        var r=Rule(); var c=(Condition)r.Entry.Children[0]; c.Left=new FieldExpression {Field=Field.Close,Timeframe=Timeframe.Parse("15m"),Offset=1};
        c.Right=new FieldExpression {Field=Field.High,Timeframe=Timeframe.Parse("D"),Offset=2};
        if(declared)r.Parameters.Add(new(){Name="unused",Default=2,Minimum=1,Maximum=3,Step=1});
        Assert.Empty(RuleStaticAnalyzer.Analyze(r));
    }
    [Fact] public void T64_StaticFindingsSurviveStrategyAndCaptureRename() {
        var r=Rule(Direction.Short); r.StrategyName="Unrelated A";
        r.References.Add(new(){Name="source A",Timeframe=r.BaseTimeframe,Trigger=RuleBuilder.AllOf(Crossing(),RuleBuilder.AlwaysTrue()),Captures=new(){new(){Id="capture A",Value=new FieldExpression {Field=Field.Close}}}});
        var before=Findings(r); r.StrategyName="Unrelated B"; r.References[0].Name="source B"; r.References[0].Captures[0].Id="capture B";
        Assert.Equal(before,Findings(r));
    }
    [Fact] public void T68_RepeatedAnalysisAndCandidateGenerationAreDeterministic() {
        var r=Rule(); r.Entry=RuleBuilder.AllOf(Crossing(),RuleBuilder.AlwaysTrue());
        Assert.Equal(Findings(r),Findings(r)); var f=Assert.Single(RuleStaticAnalyzer.Analyze(r),f=>f.Id.StartsWith("SEM001"));
        var json=RuleDefinitionJson.Serialize(r); var a=CandidateGenerator.Generate(json,f.SuggestedPatches[0]); var b=CandidateGenerator.Generate(json,f.SuggestedPatches[0]);
        Assert.True(a.IsValid); Assert.Equal(a.CandidateJson,b.CandidateJson); Assert.Equal(a.CandidateHash,b.CandidateHash); Assert.Equal(a.OriginalHash,b.OriginalHash);
    }
    [Fact] public void T64_EngineDecisionsSurviveConsistentCaptureRename() {
        (string,string) Run(string name) {var r=ReferenceRule(name,true); var o=new List<SignalObservation>();
            var result=new BacktestEngine(new BacktestEngineOptions {SignalObserver=o.Add}).Run(r,Bars());
            return(JsonSerializer.Serialize(result.Trades.Select(t=>new{t.EntryPrice,t.ExitPrice,t.GrossPnL})),JsonSerializer.Serialize(o.Select(x=>new{x.Entry,x.Exit,x.Eligibility})));}
        Assert.Equal(Run("alpha"),Run("beta"));
    }
    static RuleDefinition ReferenceRule(string id,bool overwrite) {
        var r=Rule(); r.Entry=RuleBuilder.AllOf(new Condition {Left=new ReferenceExpression {Id=id,Field="Value"},Operator=Operator.GreaterThan,Right=new NumberExpression {Value=100000}});
        r.References.Add(new(){Timeframe=r.BaseTimeframe,Trigger=RuleBuilder.AlwaysTrue(),OverwriteOnTrigger=overwrite,Captures=new(){new(){Id=id,Value=new FieldExpression {Field=Field.Close}}}}); return r;
    }
    [Theory][InlineData(false)][InlineData(true)]
    public void M2_ReferenceCaptureMetadataShowsActualOverwritePolicy(bool overwrite) {
        var o=new List<SignalObservation>(); new BacktestEngine(new BacktestEngineOptions {SignalObserver=o.Add}).Run(ReferenceRule("capture",overwrite),Bars());
        var t=o.SelectMany(x=>x.Traces).Where(t=>t.LeftReferenceId=="capture").ToArray(); Assert.NotEmpty(t);
        Assert.All(t,x=>{Assert.NotNull(x.LeftReferenceCapturedBarIndex);Assert.NotNull(x.LeftReferenceCapturedValue);});
        if(overwrite)Assert.True(t.Select(x=>x.LeftReferenceCapturedBarIndex).Distinct().Count()>1);
        else Assert.Single(t.Select(x=>x.LeftReferenceCapturedBarIndex).Distinct());
    }
    [Fact] public void M2_ReferenceAvailabilityCannotBeInFutureOfDecision() {
        var o=new List<SignalObservation>(); new BacktestEngine(new BacktestEngineOptions {SignalObserver=o.Add}).Run(ReferenceRule("capture",true),Bars());
        var t=o.SelectMany(x=>x.Traces).Where(t=>t.LeftReferenceId=="capture").ToArray(); Assert.NotEmpty(t);
        Assert.DoesNotContain(t,x=>x.LeftAvailableAt>new DateTimeOffset(x.EvaluationTimeUtc));
    }
    [Fact] public void M2_UnknownReferenceOperandIsNotFakedAsKnown() {
        var b=Bars(); var a=new TimeframeAccessor(b.InstrumentToken,b,new TimeframeCache(b));
        var evaluator=new ConditionEvaluator(new ExpressionEvaluator(a,new IndicatorProvider(a),new Dictionary<string,decimal>(),new Dictionary<string,ReferenceValue>()));
        var traces=new List<EvaluationTraceRecord>(); var r=ReferenceRule("absent",false);
        evaluator.EvaluateGroup(r.Entry,b.Timeframe,2,Start.AddMinutes(45),traces,"/entry");
        var t=Assert.Single(traces); Assert.Equal("Unknown",t.Result); Assert.Null(t.LeftValue); Assert.Null(t.LeftReferenceCapturedBarIndex);
    }
    [Fact] public void T55_SerialAndParallelSweepAreEquivalent() {
        var r=Rule(); var serial=ParameterSweep.Run(r,Bars(),Options(1),SweepCriterion.NetPnL,new[]{Binding(0,120,200,300)},null,10);
        var parallel=ParameterSweep.Run(r,Bars(),Options(4),SweepCriterion.NetPnL,new[]{Binding(0,120,200,300)},null,10);
        Assert.Equal(SweepSignature(serial),SweepSignature(parallel));
    }
    [Fact] public void M3_ReorderedGridProducesSameCanonicalRanking() {
        var r=Rule(); var a=ParameterSweep.Run(r,Bars(),Options(),SweepCriterion.Sharpe,new[]{Binding(3,2,1)},null,10);
        var b=ParameterSweep.Run(r,Bars(),Options(),SweepCriterion.Sharpe,new[]{Binding(1,3,2)},null,10); Assert.Equal(SweepSignature(a),SweepSignature(b));
    }
    [Fact] public void M3_GateRetainsInvalidTrialsAndCounts() {
        var r=Rule(); var result=ParameterSweep.Run(r,Bars(),Options(),SweepCriterion.NetPnL,new[]{Binding(0,1)},x=>Threshold(x)==1 ? new[]{"rejected threshold"} : Array.Empty<string>(),10);
        Assert.Equal(2,result.TotalCombinations); Assert.Equal(1,result.ValidCombinations); var invalid=Assert.Single(result.Ranked,x=>!x.IsValid); Assert.Contains("rejected threshold",invalid.ValidationErrors);
    }
    [Fact] public void M3_AllRejectedTrialsDoNotFabricateValidWinner() {
        var r=Rule(); ((NumberExpression)((Condition)r.Entry.Children[0]).Right).Value=0;
        var result=ParameterSweep.Run(r,Bars(),Options(),SweepCriterion.NetPnL,new[]{Binding(1,2)},x=>Threshold(x)>0 ? new[]{"not qualified"} : Array.Empty<string>(),10);
        Assert.Equal(2,result.TotalCombinations); Assert.Equal(0,result.ValidCombinations); Assert.False(result.Best.IsValid); Assert.Equal(2,result.Ranked.Count);
    }
    [Fact] public void M3_InvalidPathsRetainRejectionReason() {
        var binding=Binding(1); binding=new SweepBinding{Name=binding.Name,JsonPointer="/absent/value",Values=binding.Values};
        var result=ParameterSweep.Run(Rule(),Bars(),Options(),SweepCriterion.NetPnL,new[]{binding},null,10);
        var trial=Assert.Single(result.Ranked); Assert.False(trial.IsValid); Assert.NotEmpty(trial.ValidationErrors);
    }
    [Fact] public void M3_CancellationIsNotConvertedIntoInvalidTrial() {
        using var source=new CancellationTokenSource(); int checks=0;
        Assert.ThrowsAny<OperationCanceledException>(()=>ParameterSweep.Run(Rule(),Bars(),Options(),SweepCriterion.NetPnL,new[]{Binding(1,2)},x=> {if(Interlocked.Increment(ref checks)>1){source.Cancel();source.Token.ThrowIfCancellationRequested();}return Array.Empty<string>();},10,source.Token));
    }
    [Fact] public void M3_SweepLeavesOriginalRuleUnchanged() {
        var r=Rule(); var original=RuleDefinitionJson.Serialize(r); ParameterSweep.Run(r,Bars(),Options(4),SweepCriterion.NetPnL,new[]{Binding(100,150,200)},null,10); Assert.Equal(original,RuleDefinitionJson.Serialize(r));
    }
    [Fact] public void T46_ChangingFutureOosPricesCannotChangeSelectedTrainingParameters() {
        var r=Rule(); ((Condition)r.Entry.Children[0]).Right=new ParameterExpression{Name="threshold"};
        r.Parameters.Add(new(){Name="threshold",Default=120,Minimum=100,Maximum=180,Step=40});
        var cfg=new WalkForwardConfig {WindowCount=1,InSampleRatio=.5,FinalHoldoutRatio=0,WarmupDays=0};
        var normal=Assert.Single(WalkForward.Run(r,Bars(),Options(),cfg,10).Windows);
        var changed=Assert.Single(WalkForward.Run(r,Bars(true),Options(),cfg,10).Windows);
        Assert.Equal(normal.BestParameters.OrderBy(x=>x.Key),changed.BestParameters.OrderBy(x=>x.Key));
        Assert.Equal(normal.ISMetrics.TotalNetPnL,changed.ISMetrics.TotalNetPnL);
    }
    [Fact] public void M4_FoldsHaveDisjointScoredWindows() {
        var r=WalkForward.Run(Rule(),Bars(),Options(),new WalkForwardConfig {WindowCount=3,InSampleRatio=.6,FinalHoldoutRatio=.2,WarmupDays=1},10);
        Assert.Equal(3,r.Windows.Count); Assert.All(r.Windows,w=>Assert.True(w.ISTo<w.OOSFrom));
        for(int i=1;i<r.Windows.Count;i++)Assert.True(r.Windows[i-1].OOSTo<r.Windows[i].OOSFrom);
        Assert.True(r.Windows[^1].OOSTo<r.HoldoutFrom);
    }
    [Theory][InlineData(-.1)][InlineData(1.1)]
    public void M4_InvalidHoldoutRatioMustBeRejected(double ratio) {
        Assert.Throws<ArgumentOutOfRangeException>(()=>WalkForward.Run(Rule(),Bars(),Options(),new WalkForwardConfig {FinalHoldoutRatio=ratio},10));
    }
    [Fact] public void M4_WalkForwardIsRepeatableUnderIdenticalInputs() {
        var cfg=new WalkForwardConfig {WindowCount=2,InSampleRatio=.6,FinalHoldoutRatio=.2,WarmupDays=1};
        var first=WalkForward.Run(Rule(),Bars(),Options(),cfg,10); var second=WalkForward.Run(Rule(),Bars(),Options(),cfg,10);
        Assert.Equal(JsonSerializer.Serialize(first),JsonSerializer.Serialize(second));
    }
    [Fact] public void T42_CancelInsideEngineStopsAndPreservesOriginal() {
        using var cancellation=new CancellationTokenSource(); var rule=Rule(); var original=RuleDefinitionJson.Serialize(rule); int count=0;
        var options=new BacktestEngineOptions {SignalObserver=_=>{count++;cancellation.Cancel();}};
        Assert.ThrowsAny<OperationCanceledException>(()=>new BacktestEngine(options).Run(rule,Bars(),cancellation.Token));
        Assert.Equal(1,count); Assert.Equal(original,RuleDefinitionJson.Serialize(rule));
    }
    [Fact] public void T11_MovingCaptureCrossoverUsesCurrentCapturedThresholdForBothComparisons() {
        var rule=ReferenceRule("moving",true);
        rule.References[0].Captures[0].Value=new CalculationExpression {Op=CalculationOp.Subtract,Left=new FieldExpression{Field=Field.Close},Right=new NumberExpression{Value=.5m}};
        rule.Entry=RuleBuilder.AllOf(new Condition {Left=new FieldExpression {Field=Field.Close},Operator=Operator.CrossedAbove,Right=new ReferenceExpression {Id="moving",Field="Value"}});
        var observations=new List<SignalObservation>(); new BacktestEngine(new BacktestEngineOptions{SignalObserver=observations.Add}).Run(rule,Bars());
        var traces=observations.SelectMany(o=>o.Traces).Where(t=>t.RightReferenceId=="moving" && t.PreviousRightValue.HasValue).ToArray(); Assert.NotEmpty(traces);
        Assert.All(traces,t=>{Assert.Equal(t.RightValue,t.PreviousRightValue);Assert.Equal(t.RightValue,t.RightReferenceCapturedValue);});
    }}


