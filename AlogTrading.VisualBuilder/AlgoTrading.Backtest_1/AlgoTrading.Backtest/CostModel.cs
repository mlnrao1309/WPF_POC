using System;
using AlgoTrading.Models;

namespace AlgoTrading.Backtest;

public readonly record struct CostBreakdown(
    decimal Brokerage, decimal STT, decimal ExchangeTxn,
    decimal SEBI, decimal StampDuty, decimal GST, decimal Total);

public interface ITransactionCostModel
{
    CostBreakdown Compute(Direction direction, ExecutionMode mode,
        decimal entryPrice, decimal exitPrice, int quantity);
}

public sealed class IndianEquityCostModel : ITransactionCostModel
{
    private readonly CostConfig _cfg;
    public IndianEquityCostModel(CostConfig cfg) { _cfg = cfg ?? throw new ArgumentNullException(nameof(cfg)); }

    public CostBreakdown Compute(Direction direction, ExecutionMode mode,
        decimal entryPrice, decimal exitPrice, int quantity)
    {
        decimal q = quantity;
        decimal turnoverBuy  = direction == Direction.Long ? entryPrice * q : exitPrice  * q;
        decimal turnoverSell = direction == Direction.Long ? exitPrice  * q : entryPrice * q;

        bool intraday = mode == ExecutionMode.Intraday;
        decimal brokeragePerOrder = intraday ? _cfg.Intraday.BrokeragePerOrder : _cfg.BrokeragePerOrder;
        decimal sttBuyPct  = _cfg.SttBuyPct;
        decimal sttSellPct = intraday ? _cfg.Intraday.SttSellPct : _cfg.SttSellPct;
        decimal stampBuyPct = intraday ? _cfg.Intraday.StampDutyBuyPct : _cfg.StampDutyBuyPct;

        decimal brokerage = brokeragePerOrder * 2m;
        decimal stt = intraday
            ? turnoverSell * (sttSellPct / 100m)
            : (turnoverBuy * (sttBuyPct / 100m)) + (turnoverSell * (sttSellPct / 100m));
        decimal exchange = (turnoverBuy + turnoverSell) * (_cfg.ExchangeTxnPct / 100m);
        decimal sebi = (turnoverBuy + turnoverSell) * _cfg.SebiPerCrore / 10000000m;
        decimal stamp = turnoverBuy * (stampBuyPct / 100m);
        decimal gst = (brokerage + exchange + sebi) * (_cfg.GstPct / 100m);
        decimal total = brokerage + stt + exchange + sebi + stamp + gst;

        return new CostBreakdown(R2(brokerage), R2(stt), R2(exchange), R2(sebi), R2(stamp), R2(gst), R2(total));
    }

    private static decimal R2(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);
}