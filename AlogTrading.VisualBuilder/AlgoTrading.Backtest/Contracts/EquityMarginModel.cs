using AlgoTrading.Backtest.Configs;
using AlgoTrading.Models;

namespace AlgoTrading.Backtest.Contracts;

/// <summary>Section 5.12. requiredMargin = entryPrice * quantity * marginPercent / 100.</summary>
public sealed class EquityMarginModel : IMarginModel
{
    private readonly MarginConfig _config;

    public EquityMarginModel(MarginConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    public decimal Compute(decimal entryPrice, int quantity, ExecutionMode mode)
    {
        var block = mode == ExecutionMode.Intraday
            ? _config.Equity.Intraday
            : _config.Equity.Delivery;

        return entryPrice * quantity * block.MarginPercent / 100m;
    }
}