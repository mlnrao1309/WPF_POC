namespace Stage1Scanner.Core.Rules;

public enum RuleOperator
{
    GreaterThan,
    GreaterOrEqual,
    LessThan,
    LessOrEqual,
    Equal,
    NotEqual,
    CrossedAbove,
    CrossedBelow,
    ClosedAbove,
    ClosedBelow
}

public enum RuleSetOperator
{
    And,
    Or
}

public enum AggregateFunction
{
    Count,
    CountStreak
}

public enum ComparisonOperator
{
    GreaterThan,
    GreaterOrEqual,
    LessThan,
    LessOrEqual,
    Equal,
    NotEqual
}
