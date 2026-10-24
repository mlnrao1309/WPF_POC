using AlgoTrading.Backtest.Configs;
using AlgoTrading.Models;

namespace AlgoTrading.Backtest.Contracts;

/// <summary>Section 5.10 cost model for NSE equity cash segment.</summary>
public sealed class IndianEquityCostModel : ITransactionCostModel
{
    private readonly CostsConfig _config;

    public IndianEquityCostModel(CostsConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    public CostBreakdown Compute(TradeExecutionContext ctx)
    {
        var block = ctx.Mode == ExecutionMode.Intraday
            ? (DeliveryOrIntraday)_config.Equity.Intraday
            : (DeliveryOrIntraday)_config.Equity.Delivery;

        decimal qty = ctx.Quantity;
        decimal turnoverBuy, turnoverSell;
        if (ctx.Direction == Direction.Long)
        {
            turnoverBuy = ctx.EntryPrice * qty;
            turnoverSell = ctx.ExitPrice * qty;
        }
        else
        {
            turnoverBuy = ctx.ExitPrice * qty;
            turnoverSell = ctx.EntryPrice * qty;
        }

        if (ctx.Scope == TradeCostScope.Entry)
        {
            if (ctx.Direction == Direction.Long) turnoverSell = 0m;
            else turnoverBuy = 0m;
        }

        decimal brokerage = block.BrokeragePerOrder * (ctx.Scope == TradeCostScope.Entry ? 1m : 2m);

        decimal stt;
        if (ctx.Mode == ExecutionMode.Intraday)
            stt = block.SttSellPct / 100m * turnoverSell;
        else
            stt = block.SttBuyPct / 100m * turnoverBuy
                + block.SttSellPct / 100m * turnoverSell;

        decimal exchangeTxn = block.ExchangeTxnPct / 100m * (turnoverBuy + turnoverSell);
        decimal sebi = (turnoverBuy + turnoverSell) * block.SebiRupeesPerCrore / 10_000_000m;
        decimal stamp = block.StampDutyBuyPct / 100m * turnoverBuy;
        decimal gst = block.GstPct / 100m * (brokerage + exchangeTxn + sebi);

        return new CostBreakdown
        {
            Brokerage = Round2(brokerage),
            STT = Round2(stt),
            ExchangeTxn = Round2(exchangeTxn),
            SEBI = Round2(sebi),
            StampDuty = Round2(stamp),
            GST = Round2(gst)
        };
    }

    private static decimal Round2(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);

    private readonly struct DeliveryOrIntraday
    {
        public decimal BrokeragePerOrder { get; }
        public decimal SttBuyPct { get; }
        public decimal SttSellPct { get; }
        public decimal ExchangeTxnPct { get; }
        public decimal SebiRupeesPerCrore { get; }
        public decimal StampDutyBuyPct { get; }
        public decimal GstPct { get; }

        public static implicit operator DeliveryOrIntraday(DeliveryBlock d) => new(
            d.BrokeragePerOrder, d.SttBuyPct, d.SttSellPct, d.ExchangeTxnPct,
            d.SebiRupeesPerCrore, d.StampDutyBuyPct, d.GstPct);

        public static implicit operator DeliveryOrIntraday(IntradayBlock i) => new(
            i.BrokeragePerOrder, i.SttBuyPct, i.SttSellPct, i.ExchangeTxnPct,
            i.SebiRupeesPerCrore, i.StampDutyBuyPct, i.GstPct);

        private DeliveryOrIntraday(decimal b, decimal sb, decimal ss, decimal e,
                                    decimal s, decimal sd, decimal g)
        {
            BrokeragePerOrder = b; SttBuyPct = sb; SttSellPct = ss;
            ExchangeTxnPct = e; SebiRupeesPerCrore = s;
            StampDutyBuyPct = sd; GstPct = g;
        }
    }
}
