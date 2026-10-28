using AlgoTrading.Backtest.Cli.Bridge;
using AlgoTrading.Data;
using AlgoTrading.Models;
using Xunit;

namespace AlgoTrading.Backtest.Cli.Tests;

public class ReplayArtifactGeneratorTests
{
    private static IndicatorExpression Ema(decimal period, Field field = Field.Close) => new()
    {
        Indicator = IndicatorKind.Ema,
        Params = new() { ["period"] = period },
        Source = new FieldExpression { Field = field }
    };

    private static List<ReplayArtifactDto> Generate(params IndicatorExpression[] expressions)
    {
        var tf = Timeframe.Parse("1H");
        var bars = TestSeries.FromCloses(tf, new DateTime(2024, 1, 1, 4, 0, 0, DateTimeKind.Utc),
            Enumerable.Range(1, 60).Select(i => (decimal)i).ToArray());
        var rule = new RuleDefinition { BaseTimeframe = tf, EvaluationTimeframe = tf };
        foreach (var expression in expressions)
            rule.Entry.Children.Add(new Condition { Left = expression, Right = new NumberExpression { Value = 0 } });
        return new ReplayArtifactGenerator().Generate(Guid.NewGuid(), rule,
            new Dictionary<Timeframe, IBarSeries> { [tf] = bars }, bars)
            .Where(a => a.ArtifactKind == "Indicators").ToList();
    }

    [Fact]
    public void DifferentPeriodsHaveDifferentDatasetNames()
    {
        var artifacts = Generate(Ema(9), Ema(21));
        Assert.Equal(2, artifacts.Count);
        Assert.Equal(2, artifacts.Select(a => a.ArtifactName).Distinct().Count());
        foreach (var artifact in artifacts)
        {
            using var payload = System.Text.Json.JsonDocument.Parse(artifact.PayloadText);
            Assert.Equal(artifact.ArtifactName, payload.RootElement.GetProperty("key").GetString());
        }
    }

    [Fact]
    public void DifferentSourcesAreNotDiscarded()
    {
        var artifacts = Generate(Ema(9, Field.Close), Ema(9, Field.Open));
        Assert.Equal(2, artifacts.Count);
        Assert.NotEqual(artifacts[0].ArtifactName, artifacts[1].ArtifactName);
    }

    [Fact]
    public void ParameterOrderScaleAndExplicitTimeframeDoNotCreateDuplicates()
    {
        var first = Ema(9);
        first.Params!["unused"] = 2;
        var second = Ema(9);
        second.Params = new() { ["unused"] = 2.00m, ["period"] = 9.00m };
        second.Timeframe = Timeframe.Parse("1H");
        Assert.Single(Generate(first, second));
        Assert.Equal(Generate(first)[0].ArtifactName, Generate(second)[0].ArtifactName);
    }

    [Fact]
    public void NestedSourceOffsetsRemainDistinct()
    {
        var first = Ema(9);
        first.Source = Ema(3);
        var second = Ema(9);
        var inner = Ema(3);
        inner.Offset = 1;
        second.Source = inner;
        var artifacts = Generate(first, second);
        Assert.Equal(3, artifacts.Count); // Two outer datasets plus the shared inner dataset.
        Assert.Equal(3, artifacts.Select(a => a.ArtifactName).Distinct().Count());
    }

    [Fact]
    public void RootOffsetsShareTheExportedDataset()
    {
        var first = Ema(9);
        var second = Ema(9);
        second.Offset = 1;
        Assert.Single(Generate(first, second));
    }

    [Fact]
    public void ExportsAllPreparedCandleTimeframesAndRecordedSignals()
    {
        var tf = Timeframe.Parse("1H");
        var daily = Timeframe.Parse("D");
        var start = new DateTime(2024, 1, 1, 4, 0, 0, DateTimeKind.Utc);
        var bars = TestSeries.FromCloses(tf, start, Enumerable.Range(1, 60).Select(i => (decimal)i).ToArray());
        var days = TestSeries.FromCloses(daily, start, Enumerable.Range(1, 5).Select(i => (decimal)i).ToArray());
        var rule = new RuleDefinition { BaseTimeframe = tf, EvaluationTimeframe = tf };
        var observations = new[] { new AlgoTrading.Backtest.Runtime.SignalObservation(start, start.AddHours(1), true, true, false, false, false, false, false) };
        var artifacts = new ReplayArtifactGenerator().Generate(Guid.NewGuid(), rule,
            new Dictionary<Timeframe, IBarSeries> { [tf] = bars, [daily] = days }, bars, observations);
        Assert.Equal(2, artifacts.Count(a => a.ArtifactKind == "Candles"));
        using var payload = System.Text.Json.JsonDocument.Parse(Assert.Single(artifacts.Where(a => a.ArtifactKind == "Signals")).PayloadText);
        Assert.True(payload.RootElement.GetProperty("recordedEvaluations").GetBoolean());
        Assert.True(payload.RootElement.GetProperty("values")[1][0].GetBoolean());
        Assert.DoesNotContain(new ReplayArtifactGenerator().Generate(Guid.NewGuid(), rule,
            new Dictionary<Timeframe, IBarSeries> { [tf] = bars }, bars), a => a.ArtifactKind == "Signals");
    }

    [Fact]
    public void EngineRecordsActualEvaluationsAndSkipsIntrabarExitBars()
    {
        var tf = Timeframe.Parse("1H");
        var start = new DateTime(2024, 1, 1, 4, 0, 0, DateTimeKind.Utc);
        var bars = TestSeries.FromCloses(tf, start, 100, 100, 100, 100, 100, 100);
        var observations = new List<AlgoTrading.Backtest.Runtime.SignalObservation>();
        var rule = new RuleDefinition
        {
            BaseTimeframe = tf, EvaluationTimeframe = tf,
            Target = new TargetSpec { Mode = TargetMode.Points, Value = 0.5m }
        };
        var engine = new AlgoTrading.Backtest.Runtime.BacktestEngine(new AlgoTrading.Backtest.BacktestEngineOptions
        {
            SignalObserver = observations.Add,
            PreparedSeries = new Dictionary<Timeframe, IBarSeries> { [tf] = bars }
        });
        var result = engine.Run(rule, bars);
        Assert.NotEmpty(result.Trades);
        Assert.NotEmpty(observations);
        Assert.True(observations.Count < bars.Count - 1);
        Assert.All(observations, o =>
        {
            Assert.True(o.Entry);
            Assert.True(o.AvailableAtUtc > o.TimestampUtc);
            Assert.NotEqual(start, o.TimestampUtc);
        });
        foreach (var trade in result.Trades.Where(t => t.ExitReason == ExitReason.Target))
            Assert.DoesNotContain(observations, o => o.TimestampUtc == trade.ExitTime);
    }
}
