using System.Text.Json;

namespace AlgoTrading.Backtest.Configs;

public sealed class CostsConfig
{
    public int SchemaVersion { get; set; } = 1;
    public CostsEquityBlock Equity { get; set; } = new();

    public static CostsConfig Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Defaults();
        try
        {
            var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            return JsonSerializer.Deserialize<CostsConfig>(json, opts) ?? Defaults();
        }
        catch { return Defaults(); }
    }

    public static CostsConfig Defaults() => new()
    {
        Equity = new CostsEquityBlock
        {
            Delivery = DeliveryBlock.Default(),
            Intraday = IntradayBlock.Default()
        }
    };
}

public sealed class CostsEquityBlock
{
    public DeliveryBlock Delivery { get; set; } = DeliveryBlock.Default();
    public IntradayBlock Intraday { get; set; } = IntradayBlock.Default();
}

public sealed class DeliveryBlock
{
    public decimal BrokeragePerOrder { get; set; }
    public decimal SttBuyPct { get; set; }
    public decimal SttSellPct { get; set; }
    public decimal ExchangeTxnPct { get; set; }
    public decimal SebiRupeesPerCrore { get; set; }
    public decimal StampDutyBuyPct { get; set; }
    public decimal GstPct { get; set; }

    public static DeliveryBlock Default() => new()
    {
        BrokeragePerOrder = 0m,
        SttBuyPct = 0.100m,
        SttSellPct = 0.100m,
        ExchangeTxnPct = 0.00345m,
        SebiRupeesPerCrore = 10m,
        StampDutyBuyPct = 0.015m,
        GstPct = 18m
    };
}

public sealed class IntradayBlock
{
    public decimal BrokeragePerOrder { get; set; }
    public decimal SttBuyPct { get; set; }
    public decimal SttSellPct { get; set; }
    public decimal ExchangeTxnPct { get; set; }
    public decimal SebiRupeesPerCrore { get; set; }
    public decimal StampDutyBuyPct { get; set; }
    public decimal GstPct { get; set; }

    public static IntradayBlock Default() => new()
    {
        BrokeragePerOrder = 20m,
        SttBuyPct = 0m,
        SttSellPct = 0.025m,
        ExchangeTxnPct = 0.00345m,
        SebiRupeesPerCrore = 10m,
        StampDutyBuyPct = 0.003m,
        GstPct = 18m
    };
}