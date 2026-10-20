using System.Text.Json.Serialization;

namespace AlgoTrading.Models;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(FieldExpression),       typeDiscriminator: "field")]
[JsonDerivedType(typeof(NumberExpression),      typeDiscriminator: "number")]
[JsonDerivedType(typeof(IndicatorExpression),   typeDiscriminator: "indicator")]
[JsonDerivedType(typeof(ParameterExpression),   typeDiscriminator: "parameter")]
[JsonDerivedType(typeof(CalculationExpression), typeDiscriminator: "calculation")]
public abstract class ValueExpression
{
}

public sealed class FieldExpression : ValueExpression
{
    public int Offset { get; set; }
    public Timeframe? Timeframe { get; set; }
    public Field Field { get; set; }
}

public sealed class NumberExpression : ValueExpression
{
    public decimal Value { get; set; }
}

public sealed class IndicatorExpression : ValueExpression
{
    public int Offset { get; set; }
    public Timeframe? Timeframe { get; set; }
    public IndicatorKind Indicator { get; set; }
    public string? Output { get; set; }
    public ValueExpression? Source { get; set; }
    public Dictionary<string, decimal>? Params { get; set; }
}

public sealed class ParameterExpression : ValueExpression
{
    public string Name { get; set; } = string.Empty;
}

public sealed class CalculationExpression : ValueExpression
{
    public CalculationOp Op { get; set; }
    public ValueExpression Left { get; set; } = null!;
    public ValueExpression Right { get; set; } = null!;
}