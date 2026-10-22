// Dsl/Indicators.cs
namespace AlgoTrading.Screener.Dsl;

public static class Indicators
{
    public static decimal?[] Numeric(Series s, int n)
    {
        var r = new decimal?[n];
        for (int i = 0; i < n; i++) r[i] = s.AsNum(i);
        return r;
    }

    public static int IntArg(Series s, int n, int fallback = 0)
    {
        // Guard against n == 0 (empty candles) to prevent IndexOutOfRange
        if (n <= 0) return fallback;
        return (int)(s.AsNum(n - 1) ?? fallback);
    }
    // =====================================================================
    // Moving averages
    // =====================================================================

    public static Series Sma(Series src, int period)
    {
        int n = src.Length;
        var r = new decimal?[n];
        if (period <= 0) return Series.Num(r);
        decimal sum = 0; int cnt = 0;
        for (int i = 0; i < n; i++)
        {
            var v = src.AsNum(i);
            if (v is null) { sum = 0; cnt = 0; continue; }
            sum += v.Value; cnt++;
            if (cnt > period) { sum -= src.AsNum(i - period) ?? 0m; cnt = period; }
            if (cnt == period) r[i] = sum / period;
        }
        return Series.Num(r);
    }

    public static Series Ema(Series src, int period)
    {
        int n = src.Length;
        var r = new decimal?[n];
        if (period <= 0) return Series.Num(r);
        decimal k = 2m / (period + 1);
        decimal seedSum = 0; int seedCnt = 0; bool seeded = false;

        for (int i = 0; i < n; i++)
        {
            var v = src.AsNum(i);
            if (v is null) continue;
            if (!seeded)
            {
                seedSum += v.Value; seedCnt++;
                if (seedCnt == period) { r[i] = seedSum / period; seeded = true; }
            }
            else
            {
                r[i] = v.Value * k + r[i - 1]!.Value * (1 - k);
            }
        }
        return Series.Num(r);
    }

    public static Series Wma(Series src, int period)
    {
        int n = src.Length;
        var r = new decimal?[n];
        if (period <= 0) return Series.Num(r);
        decimal denom = period * (period + 1) / 2m;
        for (int i = period - 1; i < n; i++)
        {
            decimal sum = 0; bool ok = true;
            for (int j = 0; j < period; j++)
            {
                var v = src.AsNum(i - j);
                if (v is null) { ok = false; break; }
                sum += v.Value * (period - j);
            }
            if (ok) r[i] = sum / denom;
        }
        return Series.Num(r);
    }

    // =====================================================================
    // Momentum / oscillators
    // =====================================================================

    public static Series Rsi(Series src, int period)
    {
        int n = src.Length;
        var r = new decimal?[n];
        if (period <= 0) return Series.Num(r);

        decimal avgG = 0, avgL = 0;
        decimal gainSum = 0, lossSum = 0;
        int warm = 0; bool seeded = false;

        for (int i = 1; i < n; i++)
        {
            var cur = src.AsNum(i); var prev = src.AsNum(i - 1);
            if (cur is null || prev is null) continue;
            decimal d = cur.Value - prev.Value;
            decimal gain = d > 0 ? d : 0m;
            decimal loss = d < 0 ? -d : 0m;

            if (!seeded)
            {
                gainSum += gain; lossSum += loss; warm++;
                if (warm == period)
                {
                    avgG = gainSum / period;
                    avgL = lossSum / period;
                    seeded = true;
                    r[i] = RsiValue(avgG, avgL);
                }
            }
            else
            {
                avgG = (avgG * (period - 1) + gain) / period;
                avgL = (avgL * (period - 1) + loss) / period;
                r[i] = RsiValue(avgG, avgL);
            }
        }
        return Series.Num(r);
    }

    private static decimal? RsiValue(decimal avgGain, decimal avgLoss)
        => (avgGain == 0m && avgLoss == 0m)
            ? (decimal?)null
            : avgLoss == 0m
                ? 100m
                : 100m - 100m / (1m + avgGain / avgLoss);

    // ---- CCI ----
    public static Series Cci(Series high, Series low, Series close, int period)
    {
        int n = high.Length;
        var r = new decimal?[n];
        if (period <= 0) return Series.Num(r);

        var tp = new decimal?[n];
        for (int i = 0; i < n; i++)
        {
            var h = high.AsNum(i); var l = low.AsNum(i); var c = close.AsNum(i);
            if (h is null || l is null || c is null) continue;
            tp[i] = (h.Value + l.Value + c.Value) / 3m;
        }

        var sma = Sma(Series.Num(tp), period);

        for (int i = period - 1; i < n; i++)
        {
            var m = sma.AsNum(i);
            var t = tp[i];
            if (m is null || t is null) continue;

            decimal mdSum = 0; bool ok = true;
            for (int j = 0; j < period; j++)
            {
                var v = tp[i - j];
                if (v is null) { ok = false; break; }
                mdSum += Math.Abs(v.Value - m.Value);
            }
            if (!ok) continue;

            decimal md = mdSum / period;
            r[i] = md == 0m ? 0m : (t.Value - m.Value) / (0.015m * md);
        }
        return Series.Num(r);
    }

    // ---- Williams %R ----
    public static Series WillR(Series high, Series low, Series close, int period)
    {
        int n = high.Length;
        var r = new decimal?[n];
        var hh = Highest(high, period);
        var ll = Lowest(low, period);
        for (int i = 0; i < n; i++)
        {
            var h = hh.AsNum(i); var l = ll.AsNum(i); var c = close.AsNum(i);
            if (h is null || l is null || c is null || h.Value == l.Value) continue;
            r[i] = -100m * (h.Value - c.Value) / (h.Value - l.Value);
        }
        return Series.Num(r);
    }

    // =====================================================================
    // Volatility / bands
    // =====================================================================

    public static Series StdDev(Series src, int period)
    {
        int n = src.Length;
        var r = new decimal?[n];
        if (period <= 1) return Series.Num(r);
        var mean = Sma(src, period);
        for (int i = period - 1; i < n; i++)
        {
            decimal? m = mean.AsNum(i);
            if (m is null) continue;
            decimal sumSq = 0; bool ok = true;
            for (int j = 0; j < period; j++)
            {
                var v = src.AsNum(i - j);
                if (v is null) { ok = false; break; }
                decimal d = v.Value - m.Value;
                sumSq += d * d;
            }
            if (ok) r[i] = (decimal)Math.Sqrt((double)(sumSq / period));
        }
        return Series.Num(r);
    }

    public static Series Atr(Series high, Series low, Series close, int period)
    {
        int n = high.Length;
        var tr = new decimal?[n];
        for (int i = 0; i < n; i++)
        {
            var h = high.AsNum(i); var l = low.AsNum(i);
            if (h is null || l is null) continue;
            decimal range = h.Value - l.Value;
            if (i > 0)
            {
                var pc = close.AsNum(i - 1);
                if (pc is not null)
                {
                    decimal up = Math.Abs(h.Value - pc.Value);
                    decimal dn = Math.Abs(l.Value - pc.Value);
                    range = Math.Max(range, Math.Max(up, dn));
                }
            }
            tr[i] = range;
        }
        return WilderSmooth(Series.Num(tr), period);
    }

    public static Series SuperTrend(Series high, Series low, Series close, int period, decimal multiplier)
    {
        int n = high.Length;
        var r = new decimal?[n];
        if (period <= 0) return Series.Num(r);

        decimal initialRangeSum = 0m;
        decimal averageTrueRange = 0m;
        decimal previousUpperBand = 0m;
        decimal previousLowerBand = 0m;
        bool upwardTrend = false;

        for (int i = 0; i < n; i++)
        {
            var h = high.AsNum(i);
            var l = low.AsNum(i);
            var c = close.AsNum(i);
            if (h is null || l is null || c is null) continue;

            decimal trueRange = h.Value - l.Value;
            if (i > 0)
            {
                var previousClose = close.AsNum(i - 1);
                if (previousClose is not null)
                    trueRange = Math.Max(trueRange, Math.Max(
                        Math.Abs(h.Value - previousClose.Value),
                        Math.Abs(l.Value - previousClose.Value)));
            }

            if (i < period)
            {
                initialRangeSum += trueRange;
                if (i < period - 1) continue;
                averageTrueRange = initialRangeSum / period;
            }
            else
            {
                averageTrueRange =
                    (averageTrueRange * (period - 1) + trueRange) / period;
            }

            decimal midpoint = (h.Value + l.Value) / 2m;
            decimal upperBand = midpoint + multiplier * averageTrueRange;
            decimal lowerBand = midpoint - multiplier * averageTrueRange;

            if (i > period - 1)
            {
                var previousClose = close.AsNum(i - 1);
                if (previousClose is null) continue;

                upperBand = upperBand < previousUpperBand || previousClose.Value > previousUpperBand
                    ? upperBand
                    : previousUpperBand;
                lowerBand = lowerBand > previousLowerBand || previousClose.Value < previousLowerBand
                    ? lowerBand
                    : previousLowerBand;
                upwardTrend = upwardTrend
                    ? c.Value >= lowerBand
                    : c.Value > upperBand;
            }

            r[i] = upwardTrend ? lowerBand : upperBand;
            previousUpperBand = upperBand;
            previousLowerBand = lowerBand;
        }

        return Series.Num(r);
    }

    // ---- Keltner channels ----
    public static Series KeltnerUpper(Series high, Series low, Series close, int period, decimal multiplier)
    {
        var mid = Ema(close, period);
        var atr = Atr(high, low, close, period);
        int n = close.Length;
        var r = new decimal?[n];
        for (int i = 0; i < n; i++)
        {
            var m = mid.AsNum(i); var a = atr.AsNum(i);
            r[i] = (m is null || a is null) ? null : m.Value + multiplier * a.Value;
        }
        return Series.Num(r);
    }

    public static Series KeltnerLower(Series high, Series low, Series close, int period, decimal multiplier)
    {
        var mid = Ema(close, period);
        var atr = Atr(high, low, close, period);
        int n = close.Length;
        var r = new decimal?[n];
        for (int i = 0; i < n; i++)
        {
            var m = mid.AsNum(i); var a = atr.AsNum(i);
            r[i] = (m is null || a is null) ? null : m.Value - multiplier * a.Value;
        }
        return Series.Num(r);
    }

    public static Series KeltnerMid(Series close, int period) => Ema(close, period);

    // ---- Bollinger bands ----
    public static Series BollingerMiddle(Series src, int period) => Sma(src, period);

    public static Series BollingerUpper(Series src, int period, decimal k)
    {
        var mid = Sma(src, period);
        var sd = StdDev(src, period);
        var r = new decimal?[mid.Length];
        for (int i = 0; i < r.Length; i++)
        {
            var m = mid.AsNum(i); var s = sd.AsNum(i);
            r[i] = (m is null || s is null) ? null : m.Value + k * s.Value;
        }
        return Series.Num(r);
    }

    public static Series BollingerLower(Series src, int period, decimal k)
    {
        var mid = Sma(src, period);
        var sd = StdDev(src, period);
        var r = new decimal?[mid.Length];
        for (int i = 0; i < r.Length; i++)
        {
            var m = mid.AsNum(i); var s = sd.AsNum(i);
            r[i] = (m is null || s is null) ? null : m.Value - k * s.Value;
        }
        return Series.Num(r);
    }

    // =====================================================================
    // Trend strength — ADX family
    // =====================================================================

    private static (decimal?[] plusDi, decimal?[] minusDi, decimal?[] adx)
        ComputeAdxComponents(Series high, Series low, Series close, int period)
    {
        int n = high.Length;
        var tr      = new decimal?[n];
        var plusDm  = new decimal?[n];
        var minusDm = new decimal?[n];

        for (int i = 0; i < n; i++)
        {
            var h = high.AsNum(i); var l = low.AsNum(i);
            if (h is null || l is null) continue;

            if (i == 0)
            {
                tr[i] = h.Value - l.Value;
                plusDm[i] = 0m;
                minusDm[i] = 0m;
                continue;
            }

            var ph = high.AsNum(i - 1);
            var pl = low.AsNum(i - 1);
            var pc = close.AsNum(i - 1);
            if (ph is null || pl is null || pc is null) continue;

            decimal upMove   = h.Value - ph.Value;
            decimal downMove = pl.Value - l.Value;

            plusDm[i]  = (upMove   > downMove && upMove   > 0m) ? upMove   : 0m;
            minusDm[i] = (downMove > upMove   && downMove > 0m) ? downMove : 0m;

            decimal range = h.Value - l.Value;
            range = Math.Max(range, Math.Abs(h.Value - pc.Value));
            range = Math.Max(range, Math.Abs(l.Value - pc.Value));
            tr[i] = range;
        }

        var trSm    = WilderSmoothRaw(tr, period);
        var plusSm  = WilderSmoothRaw(plusDm, period);
        var minusSm = WilderSmoothRaw(minusDm, period);

        var plusDi  = new decimal?[n];
        var minusDi = new decimal?[n];
        var dx      = new decimal?[n];

        for (int i = 0; i < n; i++)
        {
            var t = trSm[i]; var p = plusSm[i]; var m = minusSm[i];
            if (t is null || p is null || m is null || t.Value == 0m) continue;

            decimal pd = 100m * p.Value / t.Value;
            decimal md = 100m * m.Value / t.Value;
            plusDi[i]  = pd;
            minusDi[i] = md;

            decimal sum = pd + md;
            dx[i] = sum == 0m ? 0m : 100m * Math.Abs(pd - md) / sum;
        }

        var adx = WilderSmoothRaw(dx, period);
        return (plusDi, minusDi, adx);
    }

    public static Series Adx(Series high, Series low, Series close, int period)
        => Series.Num(ComputeAdxComponents(high, low, close, period).adx);

    public static Series PlusDi(Series high, Series low, Series close, int period)
        => Series.Num(ComputeAdxComponents(high, low, close, period).plusDi);

    public static Series MinusDi(Series high, Series low, Series close, int period)
        => Series.Num(ComputeAdxComponents(high, low, close, period).minusDi);

    // =====================================================================
    // Parabolic SAR
    // =====================================================================

    public static Series Psar(Series high, Series low, decimal step, decimal max)
    {
        int n = high.Length;
        var r = new decimal?[n];
        if (n < 2) return Series.Num(r);

        var h0 = high.AsNum(0);
        var l0 = low.AsNum(0);
        if (h0 is null || l0 is null) return Series.Num(r);

        bool trendUp = true;
        decimal sar = l0.Value;
        decimal ep  = h0.Value;
        decimal af  = step;

        r[0] = sar;

        for (int i = 1; i < n; i++)
        {
            var h = high.AsNum(i);
            var l = low.AsNum(i);
            if (h is null || l is null) continue;

            decimal newSar = sar + af * (ep - sar);

            if (trendUp)
            {
                var l1 = low.AsNum(i - 1);
                if (l1 is not null) newSar = Math.Min(newSar, l1.Value);
                if (i >= 2)
                {
                    var l2 = low.AsNum(i - 2);
                    if (l2 is not null) newSar = Math.Min(newSar, l2.Value);
                }

                if (h.Value > ep) { ep = h.Value; af = Math.Min(af + step, max); }

                if (l.Value < newSar)
                {
                    trendUp = false;
                    newSar = ep;
                    ep = l.Value;
                    af = step;
                }
            }
            else
            {
                var h1 = high.AsNum(i - 1);
                if (h1 is not null) newSar = Math.Max(newSar, h1.Value);
                if (i >= 2)
                {
                    var h2 = high.AsNum(i - 2);
                    if (h2 is not null) newSar = Math.Max(newSar, h2.Value);
                }

                if (l.Value < ep) { ep = l.Value; af = Math.Min(af + step, max); }

                if (h.Value > newSar)
                {
                    trendUp = true;
                    newSar = ep;
                    ep = h.Value;
                    af = step;
                }
            }

            sar = newSar;
            r[i] = sar;
        }
        return Series.Num(r);
    }

    // =====================================================================
    // Volume indicators
    // =====================================================================

    public static Series Vwap(Series high, Series low, Series close, Series volume, Series sessionId)
    {
        int n = high.Length;
        var r = new decimal?[n];
        decimal cumPv = 0m, cumV = 0m;
        decimal? prevSession = null;

        for (int i = 0; i < n; i++)
        {
            var h = high.AsNum(i); var l = low.AsNum(i); var c = close.AsNum(i);
            var v = volume.AsNum(i);
            if (h is null || l is null || c is null || v is null) { r[i] = null; continue; }

            var s = sessionId.AsNum(i);
            if (s is not null && (prevSession is null || s.Value != prevSession.Value))
            {
                cumPv = 0m; cumV = 0m;
                prevSession = s;
            }

            decimal tp = (h.Value + l.Value + c.Value) / 3m;
            cumPv += tp * v.Value;
            cumV  += v.Value;
            r[i] = cumV == 0m ? (decimal?)null : cumPv / cumV;
        }
        return Series.Num(r);
    }

    public static Series Avwap(Series high, Series low, Series close, Series volume, Series anchor)
    {
        int n = high.Length;
        var r = new decimal?[n];
        decimal cumPv = 0m, cumV = 0m;

        for (int i = 0; i < n; i++)
        {
            var h = high.AsNum(i); var l = low.AsNum(i); var c = close.AsNum(i);
            var v = volume.AsNum(i);
            if (h is null || l is null || c is null || v is null) { r[i] = null; continue; }

            if (anchor.AsBool(i) == true) { cumPv = 0m; cumV = 0m; }

            decimal tp = (h.Value + l.Value + c.Value) / 3m;
            cumPv += tp * v.Value;
            cumV  += v.Value;
            r[i] = cumV == 0m ? (decimal?)null : cumPv / cumV;
        }
        return Series.Num(r);
    }

    public static Series Obv(Series close, Series volume)
    {
        int n = close.Length;
        var r = new decimal?[n];
        decimal cum = 0m;
        for (int i = 0; i < n; i++)
        {
            var c = close.AsNum(i); var v = volume.AsNum(i);
            if (c is null || v is null) continue;
            if (i > 0)
            {
                var pc = close.AsNum(i - 1);
                if (pc is not null)
                {
                    if (c.Value > pc.Value) cum += v.Value;
                    else if (c.Value < pc.Value) cum -= v.Value;
                }
            }
            r[i] = cum;
        }
        return Series.Num(r);
    }

    public static Series Mfi(Series high, Series low, Series close, Series volume, int period)
    {
        int n = high.Length;
        var r = new decimal?[n];
        if (period <= 0) return Series.Num(r);

        var tp    = new decimal?[n];
        var rawMf = new decimal?[n];
        for (int i = 0; i < n; i++)
        {
            var h = high.AsNum(i); var l = low.AsNum(i); var c = close.AsNum(i); var v = volume.AsNum(i);
            if (h is null || l is null || c is null || v is null) continue;
            tp[i]    = (h.Value + l.Value + c.Value) / 3m;
            rawMf[i] = tp[i]!.Value * v.Value;
        }

        for (int i = period; i < n; i++)
        {
            decimal posFlow = 0m, negFlow = 0m;
            bool ok = true;
            for (int j = i - period + 1; j <= i; j++)
            {
                if (tp[j] is null || tp[j - 1] is null || rawMf[j] is null) { ok = false; break; }
                if (tp[j]!.Value > tp[j - 1]!.Value)      posFlow += rawMf[j]!.Value;
                else if (tp[j]!.Value < tp[j - 1]!.Value) negFlow += rawMf[j]!.Value;
            }
            if (!ok) continue;
            if (negFlow == 0m) { r[i] = 100m; continue; }
            if (posFlow == 0m) { r[i] = 0m;   continue; }
            decimal mr = posFlow / negFlow;
            r[i] = 100m - 100m / (1m + mr);
        }
        return Series.Num(r);
    }

    // =====================================================================
    // Deviation, returns, z-score
    // =====================================================================

    /// <summary>Moving average deviation: (x - SMA(x, N)) / SMA(x, N) * 100.</summary>
    public static Series Mad(Series src, int period)
    {
        int n = src.Length;
        var r = new decimal?[n];
        var sma = Sma(src, period);
        for (int i = 0; i < n; i++)
        {
            var s = sma.AsNum(i); var v = src.AsNum(i);
            if (s is null || v is null || s.Value == 0m) continue;
            r[i] = (v.Value - s.Value) / s.Value * 100m;
        }
        return Series.Num(r);
    }

    /// <summary>Bar-over-bar percent change.</summary>
    public static Series Returns(Series src)
    {
        int n = src.Length;
        var r = new decimal?[n];
        for (int i = 1; i < n; i++)
        {
            var cur  = src.AsNum(i);
            var prev = src.AsNum(i - 1);
            if (cur is null || prev is null || prev.Value == 0m) continue;
            r[i] = (cur.Value - prev.Value) / prev.Value * 100m;
        }
        return Series.Num(r);
    }

    /// <summary>Rolling z-score: (x - SMA(x, N)) / STDDEV(x, N).</summary>
    public static Series ZScore(Series src, int period)
    {
        int n = src.Length;
        var r = new decimal?[n];
        var sma = Sma(src, period);
        var sd  = StdDev(src, period);
        for (int i = 0; i < n; i++)
        {
            var m = sma.AsNum(i); var s = sd.AsNum(i); var v = src.AsNum(i);
            if (m is null || s is null || v is null || s.Value == 0m) continue;
            r[i] = (v.Value - m.Value) / s.Value;
        }
        return Series.Num(r);
    }

    // =====================================================================
    // MACD
    // =====================================================================

    public static Series MacdLine(Series src, int fast, int slow) => Sub(Ema(src, fast), Ema(src, slow));

    public static Series MacdSignal(Series src, int fast, int slow, int signal)
        => Ema(MacdLine(src, fast, slow), signal);

    public static Series MacdHist(Series src, int fast, int slow, int signal)
        => Sub(MacdLine(src, fast, slow), MacdSignal(src, fast, slow, signal));

    private static Series Sub(Series a, Series b)
    {
        var r = new object?[a.Length];
        for (int i = 0; i < r.Length; i++)
        {
            var x = a.AsNum(i); var y = b.AsNum(i);
            r[i] = (x is null || y is null) ? null : x.Value - y.Value;
        }
        return new Series(r);
    }

    // =====================================================================
    // Rolling window aggregates
    // =====================================================================

    public static Series Highest(Series src, int period)
    {
        int n = src.Length; var r = new decimal?[n];
        for (int i = period - 1; i < n; i++)
        {
            decimal best = decimal.MinValue; bool ok = true;
            for (int j = 0; j < period; j++)
            {
                var v = src.AsNum(i - j);
                if (v is null) { ok = false; break; }
                if (v.Value > best) best = v.Value;
            }
            if (ok) r[i] = best;
        }
        return Series.Num(r);
    }

    public static Series Lowest(Series src, int period)
    {
        int n = src.Length; var r = new decimal?[n];
        for (int i = period - 1; i < n; i++)
        {
            decimal best = decimal.MaxValue; bool ok = true;
            for (int j = 0; j < period; j++)
            {
                var v = src.AsNum(i - j);
                if (v is null) { ok = false; break; }
                if (v.Value < best) best = v.Value;
            }
            if (ok) r[i] = best;
        }
        return Series.Num(r);
    }

    public static Series Sum(Series src, int period)
    {
        int n = src.Length; var r = new decimal?[n];
        decimal sum = 0; int cnt = 0;
        for (int i = 0; i < n; i++)
        {
            var v = src.AsNum(i);
            if (v is null) { sum = 0; cnt = 0; continue; }
            sum += v.Value; cnt++;
            if (cnt > period) { sum -= src.AsNum(i - period) ?? 0m; cnt = period; }
            if (cnt == period) r[i] = sum;
        }
        return Series.Num(r);
    }

    public static Series Avg(Series src, int period) => Sma(src, period);

    public static Series DonchianMid(Series high, Series low, int period)
    {
        int n = high.Length;
        var r = new decimal?[n];
        var hh = Highest(high, period);
        var ll = Lowest(low, period);
        for (int i = 0; i < n; i++)
        {
            var h = hh.AsNum(i); var l = ll.AsNum(i);
            if (h is null || l is null) continue;
            r[i] = (h.Value + l.Value) / 2m;
        }
        return Series.Num(r);
    }

    // =====================================================================
    // Predicate window functions
    // =====================================================================

    public static Series Count(Series predicate, int window)
    {
        int n = predicate.Length; var r = new decimal?[n];
        for (int i = window - 1; i < n; i++)
        {
            int c = 0;
            for (int j = i - window + 1; j <= i; j++)
                if (predicate.AsBool(j) == true) c++;
            r[i] = c;
        }
        return Series.Num(r);
    }

    public static Series CountStreak(Series predicate)
    {
        int n = predicate.Length; var r = new decimal?[n];
        int s = 0;
        for (int i = 0; i < n; i++)
        {
            if (predicate.AsBool(i) == true) s++; else s = 0;
            r[i] = s;
        }
        return Series.Num(r);
    }

    public static Series Any(Series predicate, int window)
    {
        int n = predicate.Length; var r = new object?[n];
        for (int i = window - 1; i < n; i++)
        {
            bool any = false;
            for (int j = i - window + 1; j <= i; j++)
                if (predicate.AsBool(j) == true) { any = true; break; }
            r[i] = any;
        }
        return new Series(r);
    }

    public static Series All(Series predicate, int window)
    {
        int n = predicate.Length; var r = new object?[n];
        for (int i = window - 1; i < n; i++)
        {
            bool all = true;
            for (int j = i - window + 1; j <= i; j++)
                if (predicate.AsBool(j) != true) { all = false; break; }
            r[i] = all;
        }
        return new Series(r);
    }

    public static Series BarsSince(Series predicate)
    {
        int n = predicate.Length;
        var r = new decimal?[n];
        int last = -1;
        for (int i = 0; i < n; i++)
        {
            if (predicate.AsBool(i) == true) last = i;
            r[i] = last >= 0 ? i - last : null;
        }
        return Series.Num(r);
    }

    public static Series BarsAgoFirst(Series predicate)
    {
        int n = predicate.Length;
        var r = new decimal?[n];
        int first = -1;
        for (int i = 0; i < n; i++)
        {
            if (first < 0 && predicate.AsBool(i) == true) first = i;
            r[i] = first >= 0 ? i - first : null;
        }
        return Series.Num(r);
    }

    // =====================================================================
    // Scalar helpers
    // =====================================================================

    public static Series Abs(Series s)    => MapNum(s, x => Math.Abs(x));
    public static Series Sqrt(Series s)   => MapNum(s, x => (decimal)Math.Sqrt((double)x));
    public static Series Log(Series s)    => MapNum(s, x => (decimal)Math.Log((double)x));
    public static Series Exp(Series s)    => MapNum(s, x => (decimal)Math.Exp((double)x));
    public static Series Round(Series s)  => MapNum(s, x => Math.Round(x, 2));
    public static Series Floor(Series s)  => MapNum(s, x => Math.Floor(x));
    public static Series Ceiling(Series s)=> MapNum(s, x => Math.Ceiling(x));

    private static Series MapNum(Series s, Func<decimal, decimal> f)
    {
        var r = new object?[s.Length];
        for (int i = 0; i < s.Length; i++)
        {
            var v = s.AsNum(i);
            r[i] = v is null ? null : f(v.Value);
        }
        return new Series(r);
    }

    // =====================================================================
    // Wilder smoothing primitive
    // =====================================================================

    private static Series WilderSmooth(Series src, int period)
        => Series.Num(WilderSmoothRaw(Numeric(src, src.Length), period));

    private static decimal?[] WilderSmoothRaw(decimal?[] src, int period)
    {
        int n = src.Length;
        var r = new decimal?[n];
        if (period <= 0) return r;

        decimal sum = 0m; int cnt = 0; bool seeded = false;
        decimal avg = 0m;
        for (int i = 0; i < n; i++)
        {
            var v = src[i];
            if (v is null) continue;
            if (!seeded)
            {
                sum += v.Value; cnt++;
                if (cnt == period) { avg = sum / period; r[i] = avg; seeded = true; }
            }
            else
            {
                avg = (avg * (period - 1) + v.Value) / period;
                r[i] = avg;
            }
        }
        return r;
    }
    // =====================================================================
    // Session-anchored values
    // =====================================================================
    // sessionId is a per-bar numeric that is constant within a session and
    // changes at the boundary (BuildFields derives it from the bar's date).
    // Both functions take an explicit sessionId so the semantics are clear
    // and match VWAP's signature.

    /// <summary>
    /// Carries the first bar's value of `src` through the whole session.
    /// On intraday data this gives the session's opening value on every bar
    /// of that session. No lookahead — the first bar's value is known as
    /// soon as it prints.
    ///
    /// Example: SESSIONOPEN(s.Open, s.SessionId)  → today's session open
    ///          SESSIONOPEN(s.Close, s.SessionId) → first 15m close of today
    /// </summary>
        // =====================================================================
    // Session boundary helpers
    // =====================================================================
    // SessionId is a per-bar numeric that is constant within a session
    // and different across sessions. BuildFields derives it from the bar's
    // date, so daily = per-bar, 15m = 26 bars per session, etc.

    /// <summary>True on the first bar of each session (where SessionId changes).</summary>
    public static Series IsFirstBarOfDay(Series sessionId)
    {
        int n = sessionId.Length;
        var r = new object?[n];

        for (int i = 0; i < n; i++)
        {
            var cur = sessionId.AsNum(i);
            if (cur is null) { r[i] = null; continue; }

            if (i == 0) { r[i] = true; continue; }

            var prev = sessionId.AsNum(i - 1);
            r[i] = prev is null || prev.Value != cur.Value;
        }

        return new Series(r);
    }

    /// <summary>True on the last bar of each session (where SessionId changes on the next bar).</summary>
    public static Series IsLastBarOfDay(Series sessionId)
    {
        int n = sessionId.Length;
        var r = new object?[n];

        for (int i = 0; i < n; i++)
        {
            var cur = sessionId.AsNum(i);
            if (cur is null) { r[i] = null; continue; }

            if (i == n - 1) { r[i] = true; continue; }

            var next = sessionId.AsNum(i + 1);
            r[i] = next is null || next.Value != cur.Value;
        }

        return new Series(r);
    }

    /// <summary>
    /// Carries the first bar's value of src through the rest of the session.
    /// On intraday data this gives the session's opening value on every bar
    /// of that session.
    /// </summary>
    public static Series SessionOpen(Series src, Series sessionId)
    {
        int n = src.Length;
        var r = new object?[n];
        object? currentSessionOpen = null;
        decimal? currentSession = null;

        for (int i = 0; i < n; i++)
        {
            var s = sessionId.AsNum(i);
            if (s is null) { r[i] = null; continue; }

            if (currentSession is null || s.Value != currentSession.Value)
            {
                currentSessionOpen = src.Values[i];
                currentSession = s;
            }

            r[i] = currentSessionOpen;
        }

        return new Series(r);
    }

    /// <summary>
    /// Carries the previous session's last value of src through the current
    /// session. On the first bar of a session this becomes that session's
    /// reference close; it stays constant until the next session begins.
    /// </summary>
    public static Series SessionClose(Series src, Series sessionId)
    {
        int n = src.Length;
        var r = new object?[n];
        object? priorSessionClose = null;
        object? lastValueOfCurrentSession = null;
        decimal? currentSession = null;

        for (int i = 0; i < n; i++)
        {
            var s = sessionId.AsNum(i);
            if (s is null) { r[i] = null; continue; }

            if (currentSession is null || s.Value != currentSession.Value)
            {
                priorSessionClose = lastValueOfCurrentSession;
                currentSession = s;
                lastValueOfCurrentSession = null;
            }

            r[i] = priorSessionClose;

            if (src.Values[i] is not null)
                lastValueOfCurrentSession = src.Values[i];
        }

        return new Series(r);
    }
}
