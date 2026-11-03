using System.Text.Json;
using AlgoTrading.Backtest;
using AlgoTrading.Backtest.Runtime;
using AlgoTrading.Backtest.Tests;
using AlgoTrading.Backtest.Cli.Bridge;
using AlgoTrading.Data;
using AlgoTrading.Models;
using Xunit;

public class ReplayEvidenceRequirementsTests
{
    static (List<ReplayArtifactDto> Artifacts,List<SignalObservation> Observations) Generate() {
        var tf=Timeframe.Parse("15m"); var bars=EngineTestSeries.FromOhlc(tf,new DateTime(2025,1,1,9,0,0,DateTimeKind.Utc),
            Enumerable.Range(0,10).Select(i=>(100m+i,101m+i,99m+i,100m+i)).ToArray());
        var rule=RuleBuilder.AlwaysEntryLong(); rule.BaseTimeframe=rule.EvaluationTimeframe=tf;
        var observations=new List<SignalObservation>(); new BacktestEngine(new BacktestEngineOptions{SignalObserver=observations.Add}).Run(rule,bars);
        return(new ReplayArtifactGenerator().Generate(Guid.NewGuid(),rule,new Dictionary<Timeframe,IBarSeries>{{tf,bars}},bars,observations),observations);
    }
    [Fact] public void Replay_PreservesGenuineGroupSignalValues() {
        var (artifacts,observations)=Generate(); var signal=Assert.Single(artifacts,a=>a.ArtifactKind=="Signals");
        using var json=JsonDocument.Parse(signal.PayloadText); var root=json.RootElement;
        Assert.Equal(observations.Count,signal.RecordCount); Assert.True(root.GetProperty("recordedEvaluations").GetBoolean());
        Assert.Equal(observations.Select(o=>o.Entry),root.GetProperty("values")[1].EnumerateArray().Select(v=>v.GetBoolean()));
    }
    [Fact] public void Replay_RetainsPerConditionOperandEvidence() {
        var (artifacts,observations)=Generate(); Assert.Contains(observations.SelectMany(o=>o.Traces),t=>t.NodePath=="/entry/children/0");
        Assert.Contains(artifacts,a=>a.PayloadText.Contains("/entry/children/0",StringComparison.Ordinal));
    }
    [Fact] public void Replay_ChunksConditionEvidenceWithoutDroppingRecords() {
        var (_, observations)=Generate();
        var trace=observations.SelectMany(o=>o.Traces).First();
        var large=observations[0] with {Traces=Enumerable.Repeat(trace,2050).ToArray()};
        var tf=Timeframe.Parse("15m");
        var bars=EngineTestSeries.FromOhlc(tf,new DateTime(2025,1,1,9,0,0,DateTimeKind.Utc),
            (100m,101m,99m,100m),(101m,102m,100m,101m));
        var rule=RuleBuilder.AlwaysEntryLong();rule.BaseTimeframe=rule.EvaluationTimeframe=tf;
        var chunks=new ReplayArtifactGenerator().Generate(Guid.NewGuid(),rule,
            new Dictionary<Timeframe,IBarSeries>{{tf,bars}},bars,new[]{large})
            .Where(a=>a.ArtifactKind=="ConditionTraces").ToArray();
        Assert.Equal(3,chunks.Length);Assert.Equal(2050,chunks.Sum(a=>a.RecordCount));
        Assert.Equal(3,chunks.Select(a=>a.ArtifactName).Distinct().Count());
        Assert.All(chunks,a=>{
            Assert.InRange(a.RecordCount,1,1024);
            using var json=JsonDocument.Parse(a.PayloadText);
            Assert.Equal(a.RecordCount,json.RootElement.GetProperty("traces").GetArrayLength());
        });
    }
}
