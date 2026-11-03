using System.Text.Json.Nodes;
using AlgoTrading.Aggregation;
using AlgoTrading.Backtest;
using AlgoTrading.Backtest.Runtime;
using AlgoTrading.Backtest.Tests;
using AlgoTrading.Models;
using AlgoTrading.Models.Analysis;
using AlgoTrading.Research.Analysis;
using Xunit;

public class MilestoneRequirementsTests
{
    static readonly DateTime Start = new(2025,1,1,9,0,0,DateTimeKind.Utc);
    static EngineTestSeries Bars(int count=50) => EngineTestSeries.FromOhlc(Timeframe.Parse("15m"), Start,
        Enumerable.Range(0,count).Select(i => (100m+i,101m+i,99m+i,100m+i)).ToArray());
    static RuleDefinition Rule() { var r=RuleBuilder.AlwaysEntryLong(new StopLossSpec { Mode=StopLossMode.Percentage, Value=2m }); r.BaseTimeframe=r.EvaluationTimeframe=Timeframe.Parse("15m"); return r; }
    static ExpressionEvaluator Expr(EngineTestSeries b) { var a=new TimeframeAccessor(b.InstrumentToken,b,new TimeframeCache(b)); return new(a,new IndicatorProvider(a),new Dictionary<string,decimal>(),new Dictionary<string,ReferenceValue>()); }
    static CandidatePatch Patch(string op,string path,JsonNode? value=null) => new() { Description="Contract test",Operations=new(){ new(){ Op=op,Path=path,Value=value } } };

    [Theory][InlineData(Operator.CrossedAbove,"GreaterThan")][InlineData(Operator.CrossedBelow,"LessThan")]
    public void M1_CrossoverProposalPreservesDirection(Operator op,string expected) {
        var r=Rule(); var c=RuleBuilder.AlwaysTrue(); c.Operator=op; r.Entry=RuleBuilder.AllOf(c,RuleBuilder.AlwaysTrue());
        var f=Assert.Single(RuleStaticAnalyzer.Analyze(r),f=>f.Id.StartsWith("SEM001"));
        Assert.Equal(expected,f.SuggestedPatches[0].Operations[0].Value!.GetValue<string>());
    }
    [Theory][InlineData(Direction.Long)][InlineData(Direction.Short)]
    public void M1_ExplicitFieldVersusLiteralHasNoTimeframeRepair(Direction direction) {
        var r=Rule(); r.Direction=direction; ((FieldExpression)((Condition)r.Entry.Children[0]).Left).Timeframe=Timeframe.Parse("D");
        Assert.DoesNotContain(RuleStaticAnalyzer.Analyze(r),f=>f.Id.StartsWith("TF001"));
    }
    [Fact] public void M1_CandidatePreservesOriginalAndValidates() {
        string json=RuleDefinitionJson.Serialize(Rule()); string original=json;
        var result=CandidateGenerator.Generate(json,Patch("replace","/strategyName",JsonValue.Create("Candidate")));
        Assert.Equal(original,json); Assert.True(result.IsValid); Assert.NotEqual(result.OriginalHash,result.CandidateHash);
    }
    [Fact] public void M1_UnsupportedPatchMustBeRejected() {
        Assert.ThrowsAny<Exception>(()=>CandidateGenerator.Generate(RuleDefinitionJson.Serialize(Rule()),Patch("invented","/strategyName")));
    }
    [Fact] public void M1_ReplaceMissingPropertyMustBeRejected() {
        Assert.ThrowsAny<Exception>(()=>CandidateGenerator.Generate(RuleDefinitionJson.Serialize(Rule()),Patch("replace","/missingProperty",JsonValue.Create(1))));
    }
    [Fact] public void M1_ArrayTestMustEnforcePrecondition() {
        var root=JsonNode.Parse(RuleDefinitionJson.Serialize(Rule()))!; root["auditArray"]=new JsonArray(1,2);
        Assert.ThrowsAny<Exception>(()=>CandidateGenerator.Generate(root.ToJsonString(),Patch("test","/auditArray/0",JsonValue.Create(99))));
    }
    [Fact] public void M1_JsonPointerEscapesTargetExistingKey() {
        var root=JsonNode.Parse(RuleDefinitionJson.Serialize(Rule()))!; root["audit/key"]=1;
        var result=CandidateGenerator.Generate(root.ToJsonString(),Patch("replace","/audit~1key",JsonValue.Create(2)));
        Assert.Equal(2,JsonNode.Parse(result.CandidateJson)!["audit/key"]!.GetValue<int>());
    }
    [Fact] public void M2_FieldAvailabilityUsesCloseTime() {
        var b=Bars(); Assert.True(Expr(b).TryEvaluate(new FieldExpression { Field=Field.Close },b.Timeframe,2,0,out _,out var at));
        Assert.Equal(new DateTimeOffset(Start.AddMinutes(45)),at);
    }
    [Fact] public void M2_CalculationWithConstantPreservesAvailability() {
        var b=Bars(); var calc=new CalculationExpression { Left=new FieldExpression {Field=Field.Close},Right=new NumberExpression {Value=2},Op=CalculationOp.Add };
        Assert.True(Expr(b).TryEvaluate(calc,b.Timeframe,2,0,out _,out var at)); Assert.Equal(new DateTimeOffset(Start.AddMinutes(45)),at);
    }
    [Fact] public void M2_EngineTraceEvaluationIsNotEarlierThanOperands() {
        var b=Bars(); var observations=new List<SignalObservation>(); var options=new BacktestEngineOptions {SignalObserver=observations.Add};
        new BacktestEngine(options).Run(Rule(),b);
        var trace=observations.SelectMany(o=>o.Traces).First(t=>t.LeftAvailableAt.HasValue);
        Assert.True(new DateTimeOffset(trace.EvaluationTimeUtc)>=trace.LeftAvailableAt,"Decision is stamped before operand availability");
    }
    [Fact] public void M2_TraceNodeHasActualJsonPointer() {
        var b=Bars(); var traces=new List<EvaluationTraceRecord>(); new ConditionEvaluator(Expr(b)).EvaluateGroup(Rule().Entry,b.Timeframe,2,Start.AddMinutes(45),traces,"/entry");
        Assert.StartsWith("/entry/children/",Assert.Single(traces).NodePath);
    }
    [Fact] public void M3_TieBreakUsesParameterValues() {
        var r=Rule(); var bindings=new[]{new SweepBinding {Name="z",JsonPointer="/entry/children/0/right/value",Values=new(){3,2,1}}};
        var result=ParameterSweep.Run(r,Bars(),RuleBuilder.ZeroCostOptions(),SweepCriterion.Sharpe,bindings,null,10);
        Assert.Equal(1m,result.Best.Parameters["z"]);
    }
    [Fact] public void M3_UnknownLiteralPathCannotBecomeValidTrial() {
        var bindings=new[]{new SweepBinding {Name="x",JsonPointer="/entry/children/0/right/unknown",Values=new(){1}}};
        var result=ParameterSweep.Run(Rule(),Bars(),RuleBuilder.ZeroCostOptions(),SweepCriterion.Sharpe,bindings,null,10);
        Assert.Equal(0,result.ValidCombinations);
    }
    [Fact] public void M4_CarryPolicyIsExplicitlyRejected() {
        Assert.Throws<NotSupportedException>(()=>WalkForward.Run(Rule(),Bars(),RuleBuilder.ZeroCostOptions(),new WalkForwardConfig {BoundaryPolicy=BoundaryPolicy.CarryToNextWindow},10));
    }
    [Fact] public void M4_ZeroHoldoutKeepsLastCandle() {
        var b=Bars(); var result=WalkForward.Run(Rule(),b,RuleBuilder.ZeroCostOptions(),new WalkForwardConfig {WindowCount=1,InSampleRatio=.5,FinalHoldoutRatio=0,WarmupDays=0},10);
        Assert.Equal(b.To,Assert.Single(result.Windows).OOSTo);
    }
    [Fact] public void M4_NoWarmupEquityBeforeTradingStart() {
        var b=Bars(); var start=Start.AddHours(5); var result=new BacktestEngine(new BacktestEngineOptions {TradingStartTime=start}).Run(Rule(),b);
        Assert.NotEmpty(result.EquityCurve); Assert.All(result.EquityCurve,p=>Assert.True(p.DateTime>=start.AddMinutes(330)));
    }
    [Fact] public void M4_EmptyFoldIsNotClaimedFrozen() {
        var result=WalkForward.Run(Rule(),Bars(1),RuleBuilder.ZeroCostOptions(),new WalkForwardConfig(),10);
        Assert.All(result.Windows,w=>Assert.False(w.TestWasFrozen));
    }
    [Fact] public void M4_HoldoutStartsAfterTiledScoringEnds() {
        var b=Bars(101); var result=WalkForward.Run(Rule(),b,RuleBuilder.ZeroCostOptions(),new WalkForwardConfig {WindowCount=1,InSampleRatio=.5,FinalHoldoutRatio=.2,WarmupDays=1},10);
        Assert.True(Assert.Single(result.Windows).OOSTo<result.HoldoutFrom);
    }
    [Fact] public void Regression_EquityKeepsInitialObservationWithoutWarmup() {
        var b=Bars(); var result=new BacktestEngine(RuleBuilder.ZeroCostOptions()).Run(Rule(),b);
        Assert.Equal(b.From.AddMinutes(330),result.EquityCurve[0].DateTime);
    }
    [Fact] public void M2_EngineEmitsEntryJsonPointer() {
        var observations=new List<SignalObservation>();
        new BacktestEngine(new BacktestEngineOptions {SignalObserver=observations.Add}).Run(Rule(),Bars());
        Assert.Contains(observations.SelectMany(o=>o.Traces),t=>t.NodePath=="/entry/children/0");
    }
    [Fact] public void M2_NestedPathPreservesDisabledSiblingIndex() {
        var b=Bars(); var disabled=RuleBuilder.AlwaysTrue(); disabled.Enabled=false;
        var group=RuleBuilder.AllOf(disabled,RuleBuilder.AllOf(RuleBuilder.AlwaysTrue()));
        var traces=new List<EvaluationTraceRecord>();
        new ConditionEvaluator(Expr(b)).EvaluateGroup(group,b.Timeframe,2,Start.AddMinutes(45),traces,"/entry");
        Assert.Equal("/entry/children/1/children/0",Assert.Single(traces).NodePath);
    }
    [Fact] public void M2_AnyGroupThreadsNestedPath() {
        var b=Bars(); var group=RuleBuilder.AllOf(RuleBuilder.AlwaysTrue()); group.Logic=GroupLogic.Any;
        var traces=new List<EvaluationTraceRecord>();
        new ConditionEvaluator(Expr(b)).EvaluateGroup(group,b.Timeframe,2,Start.AddMinutes(45),traces,"/exit");
        Assert.Equal("/exit/children/0",Assert.Single(traces).NodePath);
    }
    [Fact] public void M2_NoPathPreservesCommentFallback() {
        var b=Bars(); var c=RuleBuilder.AlwaysTrue(); c.Comment="legacy comment";
        var traces=new List<EvaluationTraceRecord>();
        new ConditionEvaluator(Expr(b)).EvaluateCondition(c,b.Timeframe,2,Start.AddMinutes(45),traces);
        Assert.Equal("legacy comment",Assert.Single(traces).NodePath);
    }
    [Theory][InlineData(false)][InlineData(true)]
    public void M2_ReferenceTriggerEvaluatorAcceptsActualPath(bool grouped) {
        var b=Bars(); Node node=grouped ? RuleBuilder.AllOf(RuleBuilder.AlwaysTrue()) : RuleBuilder.AlwaysTrue();
        var traces=new List<EvaluationTraceRecord>();
        new ConditionEvaluator(Expr(b)).Evaluate(node,b.Timeframe,2,Start.AddMinutes(45),traces,"/references/0/trigger");
        Assert.Equal(grouped ? "/references/0/trigger/children/0" : "/references/0/trigger",Assert.Single(traces).NodePath);
    }
    [Theory][InlineData(false)][InlineData(true)]
    public void M2_EngineEmitsEligibilityAndReferencePaths(bool grouped) {
        var rule=Rule(); rule.Eligibility=RuleBuilder.AllOf(RuleBuilder.AlwaysTrue());
        rule.References.Add(new ReferenceBlock {Timeframe=rule.BaseTimeframe,
            Trigger=grouped ? RuleBuilder.AllOf(RuleBuilder.AlwaysTrue()) : RuleBuilder.AlwaysTrue(),
            Captures=new(){new ReferenceCapture {Id="auditCapture",Value=new FieldExpression {Field=Field.Close}}}});
        var observations=new List<SignalObservation>();
        new BacktestEngine(new BacktestEngineOptions {SignalObserver=observations.Add}).Run(rule,Bars());
        var traces=observations.SelectMany(o=>o.Traces).ToArray();
        Assert.Contains(traces,t=>t.NodePath=="/eligibility/children/0");
        Assert.Contains(traces,t=>t.NodePath==(grouped ? "/references/0/trigger/children/0" : "/references/0/trigger"));
    }}



