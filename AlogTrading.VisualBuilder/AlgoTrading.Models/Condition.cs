using System.Text.Json.Serialization;

namespace AlgoTrading.Models;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(Condition),      typeDiscriminator: "condition")]
[JsonDerivedType(typeof(ConditionGroup), typeDiscriminator: "group")]
public abstract class Node
{
}

public sealed class Condition : Node
{
    public bool Enabled { get; set; } = true;
    public string? Comment { get; set; }
    public ValueExpression Left { get; set; } = null!;
    public Operator Operator { get; set; }
    public ValueExpression Right { get; set; } = null!;
}