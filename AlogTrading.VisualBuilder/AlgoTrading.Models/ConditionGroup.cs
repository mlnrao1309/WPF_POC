namespace AlgoTrading.Models;

public sealed class ConditionGroup : Node
{
    public bool Enabled { get; set; } = true;
    public GroupLogic Logic { get; set; } = GroupLogic.All;
    public string? Name { get; set; }
    public List<Node> Children { get; set; } = new();
}