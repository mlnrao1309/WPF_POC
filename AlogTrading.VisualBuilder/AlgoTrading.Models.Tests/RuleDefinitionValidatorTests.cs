using AlgoTrading.Models;
using FluentAssertions;
using Xunit;

namespace AlgoTrading.Models.Tests;

public class RuleDefinitionValidatorTests
{
    // Minimal valid rule helper.
    private static RuleDefinition Minimal()
    {
        var entry = new ConditionGroup { Logic = GroupLogic.All };
        entry.Children.Add(new Condition
        {
            Left  = new FieldExpression { Offset = 0, Field = Field.Close },
            Operator = Operator.GreaterThan,
            Right = new NumberExpression { Value = 0m }
        });
        return new RuleDefinition
        {
            SchemaVersion = 1,
            StrategyName = "x",
            Direction = Direction.Long,
            EvaluationTimeframe = Timeframe.Parse("D"),
            BaseTimeframe = Timeframe.Parse("D"),
            ExecutionMode = ExecutionMode.Delivery,
            Entry = entry,
            StopLoss = new StopLossSpec { Mode = StopLossMode.Percentage, Value = 2m }
        };
    }

    [Fact] public void Minimal_IsValid()
        => RuleDefinitionValidator.Validate(Minimal()).IsValid.Should().BeTrue();

    [Fact] public void Rule001_SchemaVersion()
    {
        var r = Minimal(); r.SchemaVersion = 2;
        RuleDefinitionValidator.Validate(r).Errors.Should().Contain(e => e.Code == "RULE_001");
    }

    [Fact] public void Rule002_StrategyName()
    {
        var r = Minimal(); r.StrategyName = "";
        RuleDefinitionValidator.Validate(r).Errors.Should().Contain(e => e.Code == "RULE_002");
    }

    [Fact] public void Rule009_EntryMustHaveEnabledCondition()
    {
        var r = Minimal();
        r.Entry.Children.Clear();
        RuleDefinitionValidator.Validate(r).Errors.Should().Contain(e => e.Code == "RULE_009");
    }

    [Fact] public void Rule010_MustHaveExitPath()
    {
        var r = Minimal();
        r.StopLoss = null;
        RuleDefinitionValidator.Validate(r).Errors.Should().Contain(e => e.Code == "RULE_010");
    }

    [Fact] public void Rule011_ParameterMustBeDeclared()
    {
        var r = Minimal();
        r.Entry.Children.Clear();
        r.Entry.Children.Add(new Condition
        {
            Left = new ParameterExpression { Name = "nope" },
            Operator = Operator.GreaterThan,
            Right = new NumberExpression { Value = 0m }
        });
        RuleDefinitionValidator.Validate(r).Errors.Should().Contain(e => e.Code == "RULE_011");
    }

    [Fact] public void Rule012_DuplicateParameterName()
    {
        var r = Minimal();
        r.Parameters.Add(new ParameterSpec { Name = "p", Default = 1, Minimum = 1, Maximum = 10, Step = 1 });
        r.Parameters.Add(new ParameterSpec { Name = "p", Default = 1, Minimum = 1, Maximum = 10, Step = 1 });
        RuleDefinitionValidator.Validate(r).Errors.Should().Contain(e => e.Code == "RULE_012");
    }

    [Fact] public void Rule013_ParameterRange()
    {
        var r = Minimal();
        r.Parameters.Add(new ParameterSpec { Name = "p", Default = 20, Minimum = 1, Maximum = 10, Step = 1 });
        RuleDefinitionValidator.Validate(r).Errors.Should().Contain(e => e.Code == "RULE_013");
    }

    [Fact] public void Rule016_OffsetMustBeNonNegative()
    {
        var r = Minimal();
        r.Entry.Children.Clear();
        r.Entry.Children.Add(new Condition
        {
            Left = new FieldExpression { Offset = -1, Field = Field.Close },
            Operator = Operator.GreaterThan,
            Right = new NumberExpression { Value = 0m }
        });
        RuleDefinitionValidator.Validate(r).Errors.Should().Contain(e => e.Code == "RULE_016");
    }

    [Fact] public void Rule017_GroupDepth()
    {
        var r = Minimal();
        var root = new ConditionGroup { Logic = GroupLogic.All, Enabled = true };
        var cur = root;
        for (int i = 0; i < 12; i++)
        {
            var child = new ConditionGroup { Logic = GroupLogic.All, Enabled = true };
            cur.Children.Add(child);
            cur = child;
        }
        cur.Children.Add(new Condition
        {
            Left = new FieldExpression { Offset = 0, Field = Field.Close },
            Operator = Operator.GreaterThan,
            Right = new NumberExpression { Value = 0m }
        });
        r.Entry = root;
        RuleDefinitionValidator.Validate(r).Errors.Should().Contain(e => e.Code == "RULE_017");
    }

    [Fact] public void Rule018_ExpressionDepth()
    {
        var r = Minimal();
        ValueExpression expr = new NumberExpression { Value = 0m };
        for (int i = 0; i < 12; i++)
            expr = new CalculationExpression
            {
                Op = CalculationOp.Add,
                Left = new NumberExpression { Value = 1m },
                Right = expr
            };
        r.Entry.Children.Clear();
        r.Entry.Children.Add(new Condition
        {
            Left = expr,
            Operator = Operator.GreaterThan,
            Right = new NumberExpression { Value = 0m }
        });
        RuleDefinitionValidator.Validate(r).Errors.Should().Contain(e => e.Code == "RULE_018");
    }

    [Fact] public void Rule019_InvalidName()
    {
        var r = Minimal();
        r.Entry.Name = "1bad";
        RuleDefinitionValidator.Validate(r).Errors.Should().Contain(e => e.Code == "RULE_019");
    }

    [Fact] public void Rule020_RuleModeRequiresGroup()
    {
        var r = Minimal();
        r.StopLoss = new StopLossSpec { Mode = StopLossMode.Rule, GroupName = "does.not.exist" };
        RuleDefinitionValidator.Validate(r).Errors.Should().Contain(e => e.Code == "RULE_020");
    }

    [Fact] public void Rule021_ValueMustBePositive()
    {
        var r = Minimal();
        r.StopLoss = new StopLossSpec { Mode = StopLossMode.Percentage, Value = 0m };
        RuleDefinitionValidator.Validate(r).Errors.Should().Contain(e => e.Code == "RULE_021");
    }

    [Fact] public void Rule022_EmptyEligibilityWarns()
    {
        var r = Minimal();
        r.Eligibility = new ConditionGroup { Logic = GroupLogic.All };
        RuleDefinitionValidator.Validate(r).Warnings.Should().Contain(e => e.Code == "RULE_022");
    }

    [Fact] public void Rule028_InstrumentTokenMustBeNonZero()
    {
        var r = Minimal();
        r.InstrumentToken = 0;
        RuleDefinitionValidator.Validate(r).Errors.Should().Contain(e => e.Code == "RULE_028");
    }

    [Fact] public void Rule029_SkippedWithoutLookup()
    {
        var r = Minimal();
        r.InstrumentToken = 2885;
        var res = RuleDefinitionValidator.Validate(r, null);
        res.SkippedChecks.Should().Contain("RULE_029");
    }

    [Fact] public void Rule030_TunedWithSweepWarns()
    {
        var r = Minimal();
        r.InstrumentToken = 2885;
        r.TargetTimeframe = Timeframe.Parse("15m");
        r.Parameters.Add(new ParameterSpec { Name = "p", Default = 5, Minimum = 1, Maximum = 10, Step = 1 });
        RuleDefinitionValidator.Validate(r).Warnings.Should().Contain(e => e.Code == "RULE_030");
    }

    [Fact] public void Rule031_OpenedRequiresOpenField()
    {
        var r = Minimal();
        r.Entry.Children.Clear();
        r.Entry.Children.Add(new Condition
        {
            Left = new FieldExpression { Offset = 0, Field = Field.Close },
            Operator = Operator.OpenedAbove,
            Right = new NumberExpression { Value = 0m }
        });
        RuleDefinitionValidator.Validate(r).Errors.Should().Contain(e => e.Code == "RULE_031");
    }
}