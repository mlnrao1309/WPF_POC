// Wpf/CompletionCatalog.cs
namespace AlgoTrading.Screener.Wpf;

public sealed class CompletionCatalog
{

    public IReadOnlyList<string> Keywords { get; init; } = new[]
    {
        "from","in","let","where","orderby","asc","desc","select","as","take","per",
        "and","or","not","true","false",
        "greaterthan","greaterthanorequal","greaterthanequalto","greaterthanorequalto",
        "lessthan","lessthanorequal","lessthanequalto",
        "equals","notequals","between",
        "crossedabove","crossedbelow","crossedover","crossedunder",
        "closedabove","closedbelow","touchedabove","touchedbelow"
    };

    public IReadOnlyList<string> Collections { get; init; } = new[] { "Instruments" };

    public IReadOnlyList<string> Fields { get; init; } = new[]
    {
        "Open","High","Low","Close","Volume","OI","Time","Symbol"
    };

    public IReadOnlyList<string> ContextVars { get; init; } = new[] { "s" };

    public IReadOnlyList<FnSignature> Functions { get; init; } = new[]
    {
        new FnSignature("SMA",        "SMA(series, period)"),
        new FnSignature("EMA",        "EMA(series, period)"),
        new FnSignature("WMA",        "WMA(series, period)"),
        new FnSignature("RSI",        "RSI(series, period)"),
        new FnSignature("STDDEV",     "STDDEV(series, period)"),
        new FnSignature("ROC",        "ROC(series, period)"),
        new FnSignature("BBMIDDLE",   "BBMIDDLE(series, period)"),
        new FnSignature("BBUPPER",    "BBUPPER(series, period[, k])"),
        new FnSignature("BBLOWER",    "BBLOWER(series, period[, k])"),
        new FnSignature("MACD",       "MACD(series, fast, slow)"),
        new FnSignature("MACDSIGNAL", "MACDSIGNAL(series, fast, slow, signal)"),
        new FnSignature("MACDHIST",   "MACDHIST(series, fast, slow, signal)"),
        new FnSignature("ATR",        "ATR(high, low, close, period)"),
        new FnSignature("SUPERTREND", "SUPERTREND(period, multiplier) or SUPERTREND(high, low, close, period[, multiplier])"),
        new FnSignature("HIGHEST",    "HIGHEST(series, period)"),
        new FnSignature("LOWEST",     "LOWEST(series, period)"),
        new FnSignature("SUM",        "SUM(series, period)"),
        new FnSignature("AVG",        "AVG(series, period)"),
        new FnSignature("MIN",        "MIN(series, period)"),
        new FnSignature("MAX",        "MAX(series, period)"),
        new FnSignature("COUNT",      "COUNT(predicate, window)"),
        new FnSignature("COUNTSTREAK","COUNTSTREAK(predicate)"),
        new FnSignature("ANY",        "ANY(predicate, window)"),
        new FnSignature("ALL",        "ALL(predicate, window)"),
        new FnSignature("ABS",        "ABS(series)"),
        new FnSignature("SQRT",       "SQRT(series)"),
        new FnSignature("LOG",        "LOG(series)"),
        new FnSignature("EXP",        "EXP(series)"),
        new FnSignature("ROUND",      "ROUND(series)"),
        new FnSignature("FLOOR",      "FLOOR(series)"),
        new FnSignature("CEILING",    "CEILING(series)")
    };

    public sealed record FnSignature(string Name, string Signature);
}
