using AlgoTrading.Models;
using FluentAssertions;
using Xunit;

namespace AlgoTrading.Models.Tests;

public class RuleDefinitionJsonTests
{
    private static string SamplePath(int n)
        => Path.Combine(AppContext.BaseDirectory, "samples", "rules", $"example-{n:00}.json");

    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)]
    [InlineData(4)] [InlineData(5)] [InlineData(6)]
    [InlineData(7)] [InlineData(8)] [InlineData(9)]
    public void Sample_RoundTrips(int n)
    {
        var path = SamplePath(n);
        File.Exists(path).Should().BeTrue($"sample {path} must exist");

        var rule = RuleDefinitionJson.LoadFromFile(path);
        var json1 = RuleDefinitionJson.Serialize(rule);
        var rule2 = RuleDefinitionJson.Deserialize(json1);
        var json2 = RuleDefinitionJson.Serialize(rule2);

        json1.Should().Be(json2);
    }

    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)]
    [InlineData(4)] [InlineData(5)] [InlineData(6)]
    [InlineData(7)] [InlineData(8)] [InlineData(9)]
    public void Sample_Validates(int n)
    {
        var path = SamplePath(n);
        var rule = RuleDefinitionJson.LoadFromFile(path);
        var res = RuleDefinitionValidator.Validate(rule);
        res.IsValid.Should().BeTrue(
            "errors: " + string.Join("; ", res.Errors.Select(e => e.ToString())));
    }

    [Fact]
    public void RejectsUnknownSchemaVersion()
    {
        var json = """
        {
          "schemaVersion": 2,
          "strategyName": "x",
          "direction": "Long",
          "evaluationTimeframe": "D",
          "executionMode": "Delivery",
          "baseTimeframe": "D",
          "entry": { "kind": "group", "enabled": true, "logic": "All", "children": [] }
        }
        """;
        var rule = RuleDefinitionJson.Deserialize(json);
        var res = RuleDefinitionValidator.Validate(rule);
        res.IsValid.Should().BeFalse();
        res.Errors.Should().Contain(e => e.Code == "RULE_001");
    }
}