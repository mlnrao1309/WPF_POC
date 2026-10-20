using AlgoTrading.Models;

namespace AlgoTrading.Backtest.Runtime;

internal sealed class PositionState
{
    public bool IsOpen { get; set; }
    public Direction Direction { get; set; }
    public DateTime EntryTime { get; set; }
    public decimal EntryPrice { get; set; }
    public int EntryBarIndex { get; set; }
    public int Quantity { get; set; }
    public decimal? StopLossPrice { get; set; }
    public decimal? TargetPrice { get; set; }
    public ConditionGroup? StopLossRuleGroup { get; set; }
    public ConditionGroup? TargetRuleGroup { get; set; }
    public decimal MAE { get; set; }
    public decimal MFE { get; set; }

    public void Reset()
    {
        IsOpen = false;
        Direction = Direction.Long;
        EntryTime = default;
        EntryPrice = 0m;
        EntryBarIndex = 0;
        Quantity = 0;
        StopLossPrice = null;
        TargetPrice = null;
        StopLossRuleGroup = null;
        TargetRuleGroup = null;
        MAE = 0m;
        MFE = 0m;
    }

    public void UpdateExcursions(decimal high, decimal low)
    {
        if (Direction == Direction.Long)
        {
            var up = high - EntryPrice;
            var down = EntryPrice - low;
            if (up > MFE) MFE = up;
            if (down > MAE) MAE = down;
        }
        else
        {
            var up = EntryPrice - low;
            var down = high - EntryPrice;
            if (up > MFE) MFE = up;
            if (down > MAE) MAE = down;
        }
    }
}