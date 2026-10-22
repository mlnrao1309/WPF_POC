using System.Diagnostics;

namespace AlgoTrading.Screener.Dsl;

public static class Functions
{
    public static Dictionary<string, Func<IReadOnlyList<Series>, int, Series>> Build()
    {
        var d = new Dictionary<string, Func<IReadOnlyList<Series>, int, Series>>(
            StringComparer.OrdinalIgnoreCase);

        // ---- moving averages ----
        d["SMA"] = (a, n) => Indicators.Sma(a[0], Indicators.IntArg(a[1], n));
        d["EMA"] = (a, n) => Indicators.Ema(a[0], Indicators.IntArg(a[1], n));
        d["WMA"] = (a, n) => Indicators.Wma(a[0], Indicators.IntArg(a[1], n));

        // ---- momentum ----
        d["RSI"]    = (a, n) => Indicators.Rsi(a[0], Indicators.IntArg(a[1], n, 14));
        d["STDDEV"] = (a, n) => Indicators.StdDev(a[0], Indicators.IntArg(a[1], n));
        d["ROC"]    = (a, n) =>
        {
            int p = Indicators.IntArg(a[1], n);
            var src = a[0];
            var r = new object?[n];
            for (int i = p; i < n; i++)
            {
                var cur  = src.AsNum(i);
                var prev = src.AsNum(i - p);
                r[i] = (cur is null || prev is null || prev.Value == 0m)
                    ? null
                    : (cur.Value - prev.Value) / prev.Value * 100m;
            }
            return new Series(r);
        };

        // ---- volatility / bands ----
        d["BBMIDDLE"] = (a, n) => Indicators.BollingerMiddle(a[0], Indicators.IntArg(a[1], n));
        d["BBUPPER"]  = (a, n) =>
        {
            int period = Indicators.IntArg(a[1], n);
            decimal k  = a.Count > 2 ? (a[2].LastNum() ?? 2m) : 2m;
            return Indicators.BollingerUpper(a[0], period, k);
        };
        d["BBLOWER"]  = (a, n) =>
        {
            int period = Indicators.IntArg(a[1], n);
            decimal k  = a.Count > 2 ? (a[2].LastNum() ?? 2m) : 2m;
            return Indicators.BollingerLower(a[0], period, k);
        };

        // ---- MACD family ----
        d["MACD"] = (a, n) => Indicators.MacdLine(
            a[0],
            Indicators.IntArg(a[1], n, 12),
            Indicators.IntArg(a[2], n, 26));
        d["MACDSIGNAL"] = (a, n) => Indicators.MacdSignal(
            a[0],
            Indicators.IntArg(a[1], n, 12),
            Indicators.IntArg(a[2], n, 26),
            Indicators.IntArg(a[3], n, 9));
        d["MACDHIST"] = (a, n) => Indicators.MacdHist(
            a[0],
            Indicators.IntArg(a[1], n, 12),
            Indicators.IntArg(a[2], n, 26),
            Indicators.IntArg(a[3], n, 9));

        // ---- ATR / SuperTrend ----
        d["ATR"] = (a, n) => Indicators.Atr(
            a[0], a[1], a[2],
            Indicators.IntArg(a[3], n, 14));

        d["SUPERTREND"] = (a, n) => Indicators.SuperTrend(
            a[0], a[1], a[2],
            Indicators.IntArg(a[3], n, 10),
            a.Count > 4 ? (a[4].LastNum() ?? 3m) : 3m);

        // ---- rolling aggregates ----
        d["HIGHEST"] = (a, n) => Indicators.Highest(a[0], Indicators.IntArg(a[1], n));
        d["LOWEST"]  = (a, n) => Indicators.Lowest (a[0], Indicators.IntArg(a[1], n));
        d["SUM"]     = (a, n) => Indicators.Sum    (a[0], Indicators.IntArg(a[1], n));
        d["AVG"]     = (a, n) => Indicators.Avg    (a[0], Indicators.IntArg(a[1], n));
        d["MIN"]     = (a, n) => Indicators.Lowest (a[0], Indicators.IntArg(a[1], n));
        d["MAX"]     = (a, n) => Indicators.Highest(a[0], Indicators.IntArg(a[1], n));

        // ---- predicate windows ----
        d["COUNT"]       = (a, n) => Indicators.Count(a[0], Indicators.IntArg(a[1], n));
        d["COUNTSTREAK"] = (a, n) => Indicators.CountStreak(a[0]);
        // Guard against missing arguments
        d["ANY"] = (a, n) =>
        {
            Debug.WriteLine($"{a}, {n}" );
            if (a.Count < 2) throw new ArgumentException("ANY requires 2 arguments: a predicate and a window size. Example: ANY(ema5 crossedbelow ema20, 5)");
            return Indicators.Any(a[0], Indicators.IntArg(a[1], n));
        };
        d["ALL"]         = (a, n) => Indicators.All(a[0], Indicators.IntArg(a[1], n));

        // ---- NEW: bars since ----
        d["BARSSINCE"] = (a, n) => Indicators.BarsSince(a[0]);
        d["BARSAGOFIRST"] = (a, n) => Indicators.BarsAgoFirst(a[0]);
        // ---- scalar ----
        d["ABS"]     = (a, n) => Indicators.Abs(a[0]);
        d["SQRT"]    = (a, n) => Indicators.Sqrt(a[0]);
        d["LOG"]     = (a, n) => Indicators.Log(a[0]);
        d["EXP"]     = (a, n) => Indicators.Exp(a[0]);
        d["ROUND"]   = (a, n) => Indicators.Round(a[0]);
        d["FLOOR"]   = (a, n) => Indicators.Floor(a[0]);
        d["CEILING"] = (a, n) => Indicators.Ceiling(a[0]);

        // ---- session helpers ----
        d["ISFIRSTBAROFDAY"] = (a, n) => Indicators.IsFirstBarOfDay(a[0]);
        d["ISLASTBAROFDAY"] = (a, n) => Indicators.IsLastBarOfDay(a[0]);
        d["SESSIONOPEN"] = (a, n) => Indicators.SessionOpen(a[0], a[1]);
        d["SESSIONCLOSE"] = (a, n) => Indicators.SessionClose(a[0], a[1]);

        return d;
    }
}