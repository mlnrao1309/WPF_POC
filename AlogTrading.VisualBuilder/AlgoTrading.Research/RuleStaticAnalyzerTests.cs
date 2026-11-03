using AlgoTrading.Models;
using AlgoTrading.Models.Analysis;
using FluentAssertions;
using Xunit;

namespace AlgoTrading.Models.Tests;

public class RuleStaticAnalyzerTests
{
    [Fact]
    public void Detect_EventFilterConflict()
    {
        var rule = new RuleDefinition
        {
            SchemaVersion = 1,
            StrategyName = "EventFilterTest",
            BaseTimeframe = Timeframe.Parse("15m"),
            EvaluationTimeframe = Timeframe.Parse("15m"),
            Entry = new ConditionGroup
            {
                Logic = GroupLogic.All,
                Children = 
                {
                    new Condition 
                    { 
                        Left = new FieldExpression { Field = Field.Close }, 
                        Operator = Operator.CrossedAbove, 
                        Right = new NumberExpression { Value = 100m } 
                    },
                    new Condition 
                    { 
                        Left = new IndicatorExpression { Indicator = IndicatorKind.Rsi, Params = new() { { "period", 14 } } }, 
                        Operator = Operator.GreaterThan, 
                        Right = new NumberExpression { Value = 50m } 
                    }
                }
            }
        };

        var findings = RuleStaticAnalyzer.Analyze(rule);
        
        findings.Should().Contain(f => f.Id.StartsWith("SEM001"));
        var finding = findings.First(f => f.Id.StartsWith("SEM001"));
        finding.SuggestedPatches.Should().NotBeEmpty();
        finding.SuggestedPatches[0].Operations[0].Path.Should().Be("/entry/children/0/operator");
        finding.SuggestedPatches[0].Operations[0].Value.ToString().Should().Be("GreaterThan");
    }

    [Fact]
    public void Detect_MissingTimeframe()
    {
        var rule = new RuleDefinition
        {
            SchemaVersion = 1,
            StrategyName = "TimeframeTest",
            BaseTimeframe = Timeframe.Parse("15m"),
            EvaluationTimeframe = Timeframe.Parse("15m"),
            Entry = new ConditionGroup
            {
                Logic = GroupLogic.All,
                Children = 
                {
                    new Condition 
                    { 
                        Left = new FieldExpression { Field = Field.Close, Timeframe = null }, 
                        Operator = Operator.GreaterThan, 
                        Right = new FieldExpression { Field = Field.High, Timeframe = Timeframe.Parse("D") } 
                    }
                }
            }
        };

        var findings = RuleStaticAnalyzer.Analyze(rule);
        
        findings.Should().Contain(f => f.Id.StartsWith("TF001"));
        var finding = findings.First(f => f.Id.StartsWith("TF001"));
        finding.JsonPointer.Should().Be("/entry/children/0/left");
        finding.SuggestedPatches[0].Operations[0].Value.ToString().Should().Be("D");
    }

    [Fact]
    public void GenerateCandidate_AppliesPatchAndValidates()
    {
        var rule = new RuleDefinition
        {
            SchemaVersion = 1,
            StrategyName = "PatchTest",
            BaseTimeframe = Timeframe.Parse("15m"),
            EvaluationTimeframe = Timeframe.Parse("15m"),
            Entry = new ConditionGroup
            {
                Logic = GroupLogic.All,
                Children = 
                {
                    new Condition 
                    { 
                        Left = new FieldExpression { Field = Field.Close, Timeframe = null }, 
                        Operator = Operator.GreaterThan, 
                        Right = new FieldExpression { Field = Field.High, Timeframe = Timeframe.Parse("D") } 
                    }
                }
            },
            StopLoss = new StopLossSpec { Mode = StopLossMode.Percentage, Value = 2m }
        };
        var json = RuleDefinitionJson.Serialize(rule);

        var patch = new CandidatePatch
        {
            Description = "Fix timeframe",
            Operations = new()
            {
                new() { Op = "add", Path = "/entry/children/0/left/timeframe", Value = System.Text.Json.Nodes.JsonValue.Create("D") }
            }
        };

        var result = CandidateGenerator.Generate(json, patch);
        
        result.IsValid.Should().BeTrue();
        result.OriginalHash.Should().NotBe(result.CandidateHash);
        
        var newRule = RuleDefinitionJson.Deserialize(result.CandidateJson);
        newRule.Entry.Children[0].Should().BeOfType<Condition>()
            .Which.Left.Should().BeOfType<FieldExpression>()
            .Which.Timeframe.ToString().Should().Be("D");
    }
}