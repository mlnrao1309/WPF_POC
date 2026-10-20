using System;
using AlgoTrading.Models;

namespace AlgoTrading.Backtest;

public interface IMarginModel { decimal RequiredMargin(decimal entryPrice, int quantity, ExecutionMode mode); }

public sealed class EquityMarginModel : IMarginModel
{
    private readonly MarginConfig _c;
    public EquityMarginModel(MarginConfig c) { _c = c ?? throw new ArgumentNullException(nameof(c)); }
    public decimal RequiredMargin(decimal p, int q, ExecutionMode m)
    {
        decimal n = Math.Abs(p) * q;
        return m switch
        {
            ExecutionMode.Delivery => n * (_c.DeliveryMarginPct / 100m),
            ExecutionMode.Intraday => n / _c.IntradayLeverage,
            _ => n,
        };
    }
}