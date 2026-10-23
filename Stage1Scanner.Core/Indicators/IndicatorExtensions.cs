using Stage1Scanner.Core.Market;
using Stage1Scanner.Core.Rules;
using Stage1Scanner.Core.Pivots;
using Stage1Scanner.Core.Series;

namespace Stage1Scanner.Core.Indicators;

public static class IndicatorExtensions
{
    public static NumericSeries SMA(
        this Candles candles,
        int period,
        SourceField source = SourceField.Close)
    {
        var key = $"SMA|{period}|{source}";
        return IndicatorCache.GetOrCreate(candles, key, () =>
        {
            ValidatePeriod(period);
            var input = Select(candles, source);
            var output = new decimal?[input.Length];

            decimal sum = 0m;
            var nullsInWindow = 0;

            for (var i = 0; i < input.Length; i++)
            {
                if (input[i].HasValue)
                {
                    sum += input[i]!.Value;
                }
                else
                {
                    nullsInWindow++;
                }

                if (i >= period)
                {
                    if (input[i - period].HasValue)
                    {
                        sum -= input[i - period]!.Value;
                    }
                    else
                    {
                        nullsInWindow--;
                    }
                }

                if (i >= period - 1 && nullsInWindow == 0)
                {
                    output[i] = sum / period;
                }
            }

            return new NumericSeries(candles, output, $"SMA({period})");
        });
    }

    public static NumericSeries EMA(
        this Candles candles,
        int period,
        SourceField source = SourceField.Close)
    {
        var key = $"EMA|{period}|{source}";
        return IndicatorCache.GetOrCreate(candles, key, () =>
        {
            ValidatePeriod(period);
            var input = Select(candles, source);
            var output = Ema(input, period);
            return new NumericSeries(candles, output, $"EMA({period})");
        });
    }

    public static NumericSeries DEMA(
        this Candles candles,
        int period,
        SourceField source = SourceField.Close)
    {
        var key = $"DEMA|{period}|{source}";
        return IndicatorCache.GetOrCreate(candles, key, () =>
        {
            var input = Select(candles, source);
            var ema1 = Ema(input, period);
            var ema2 = Ema(ema1, period);
            var output = new decimal?[input.Length];

            for (var i = 0; i < output.Length; i++)
            {
                if (ema1[i].HasValue && ema2[i].HasValue)
                {
                    output[i] = 2m * ema1[i]!.Value - ema2[i]!.Value;
                }
            }

            return new NumericSeries(candles, output, $"DEMA({period})");
        });
    }

    public static NumericSeries TEMA(
        this Candles candles,
        int period,
        SourceField source = SourceField.Close)
    {
        var key = $"TEMA|{period}|{source}";
        return IndicatorCache.GetOrCreate(candles, key, () =>
        {
            var input = Select(candles, source);
            var ema1 = Ema(input, period);
            var ema2 = Ema(ema1, period);
            var ema3 = Ema(ema2, period);
            var output = new decimal?[input.Length];

            for (var i = 0; i < output.Length; i++)
            {
                if (ema1[i].HasValue && ema2[i].HasValue && ema3[i].HasValue)
                {
                    output[i] =
                        3m * ema1[i]!.Value -
                        3m * ema2[i]!.Value +
                        ema3[i]!.Value;
                }
            }

            return new NumericSeries(candles, output, $"TEMA({period})");
        });
    }

    public static NumericSeries WMA(
        this Candles candles,
        int period,
        SourceField source = SourceField.Close)
    {
        var key = $"WMA|{period}|{source}";
        return IndicatorCache.GetOrCreate(candles, key, () =>
        {
            var input = Select(candles, source);
            var output = Wma(input, period);
            return new NumericSeries(candles, output, $"WMA({period})");
        });
    }

    public static NumericSeries HMA(
        this Candles candles,
        int period,
        SourceField source = SourceField.Close)
    {
        var key = $"HMA|{period}|{source}";
        return IndicatorCache.GetOrCreate(candles, key, () =>
        {
            ValidatePeriod(period);
            var input = Select(candles, source);

            var half = Math.Max(1, (int)Math.Round(period / 2m, MidpointRounding.AwayFromZero));
            var sqrt = Math.Max(1, (int)Math.Round(Math.Sqrt(period)));
            var wmaHalf = Wma(input, half);
            var wmaFull = Wma(input, period);
            var diff = new decimal?[input.Length];

            for (var i = 0; i < diff.Length; i++)
            {
                if (wmaHalf[i].HasValue && wmaFull[i].HasValue)
                {
                    diff[i] = 2m * wmaHalf[i]!.Value - wmaFull[i]!.Value;
                }
            }

            var output = Wma(diff, sqrt);
            return new NumericSeries(candles, output, $"HMA({period})");
        });
    }

    /// <summary>
    /// Wilder RSI with optional smoothing of the RSI line and optional
    /// pivot-based regular divergence detection.
    /// </summary>
    /// <remarks>
    /// Divergence semantics: a pivot at bar i is confirmed at bar i + pivotLookback,
    /// and the divergence is stamped on the confirmation bar. Bullish divergence =
    /// lower price low with higher RSI low. Bearish divergence = higher price high
    /// with lower RSI high. Strict inequalities.
    /// </remarks>
    public static RsiSeries RSI(
        this Candles candles,
        int parameterPeriod = 14,
        MovingAverageType? smoothing = null,
        int smoothingPeriod = 14,
        bool calculateDivergence = false,
        int divergencePivotLookback = 5,
        int divergenceMaxBarsBetweenPivots = 60,
        SourceField source = SourceField.Close)
    {
        var key =
            $"RSI|{parameterPeriod}|{smoothing}|{smoothingPeriod}|" +
            $"{calculateDivergence}|{divergencePivotLookback}|" +
            $"{divergenceMaxBarsBetweenPivots}|{source}";

        return IndicatorCache.GetOrCreate(candles, key, () =>
        {
            ValidatePeriod(parameterPeriod);
            var input = Select(candles, source);
            var rsiValues = Rsi(input, parameterPeriod);

            NumericSeries? smoothSeries = null;

            if (smoothing.HasValue)
            {
                ValidatePeriod(smoothingPeriod);

                var smoothed = smoothing.Value switch
                {
                    MovingAverageType.SMA => SmaNullable(rsiValues, smoothingPeriod),
                    MovingAverageType.EMA => Ema(rsiValues, smoothingPeriod),
                    MovingAverageType.WMA => Wma(rsiValues, smoothingPeriod),
                    _ => throw new NotSupportedException(
                        $"RSI smoothing type '{smoothing.Value}' is not supported. " +
                        "Supported: SMA, EMA, WMA.")
                };

                smoothSeries = new NumericSeries(
                    candles, smoothed, $"RSI.Smooth({smoothingPeriod})");
            }

            var valueSeries = new NumericSeries(
                candles, rsiValues, $"RSI({parameterPeriod})");

            RsiDivergenceType?[] divergence;

            if (calculateDivergence)
            {
                ValidatePeriod(divergencePivotLookback);
                ValidatePeriod(divergenceMaxBarsBetweenPivots);

                divergence = DetectRsiDivergence(
                    candles,
                    valueSeries,
                    divergencePivotLookback,
                    divergenceMaxBarsBetweenPivots);
            }
            else
            {
                divergence = new RsiDivergenceType?[candles.Count];
            }

            return new RsiSeries
            {
                Value = valueSeries,
                Smooth = smoothSeries,
                Divergence = divergence
            };
        });
    }

    public static MacdSeries MACD(
        this Candles candles,
        int fastPeriod = 12,
        int slowPeriod = 26,
        int signalPeriod = 9,
        SourceField source = SourceField.Close)
    {
        var key = $"MACD|{fastPeriod}|{slowPeriod}|{signalPeriod}|{source}";
        return IndicatorCache.GetOrCreate(candles, key, () =>
        {
            var input = Select(candles, source);
            var fast = Ema(input, fastPeriod);
            var slow = Ema(input, slowPeriod);
            var macd = new decimal?[input.Length];

            for (var i = 0; i < input.Length; i++)
            {
                if (fast[i].HasValue && slow[i].HasValue)
                {
                    macd[i] = fast[i]!.Value - slow[i]!.Value;
                }
            }

            var signal = Ema(macd, signalPeriod);
            var histogram = new decimal?[input.Length];

            for (var i = 0; i < input.Length; i++)
            {
                if (macd[i].HasValue && signal[i].HasValue)
                {
                    histogram[i] = macd[i]!.Value - signal[i]!.Value;
                }
            }

            return new MacdSeries
            {
                Macd = new NumericSeries(candles, macd, "MACD"),
                Signal = new NumericSeries(candles, signal, "MACD.Signal"),
                Histogram = new NumericSeries(candles, histogram, "MACD.Histogram")
            };
        });
    }

    public static BollingerBandSeries BollingerBands(
        this Candles candles,
        int period = 20,
        decimal multiplier = 2m,
        SourceField source = SourceField.Close)
    {
        var key = $"BB|{period}|{multiplier}|{source}";
        return IndicatorCache.GetOrCreate(candles, key, () =>
        {
            var input = Select(candles, source);
            var basis = SmaNullable(input, period);
            var upper = new decimal?[input.Length];
            var lower = new decimal?[input.Length];

            for (var i = period - 1; i < input.Length; i++)
            {
                if (!basis[i].HasValue)
                {
                    continue;
                }

                var mean = basis[i]!.Value;
                var sumSq = 0d;
                var valid = true;

                for (var j = i - period + 1; j <= i; j++)
                {
                    if (!input[j].HasValue)
                    {
                        valid = false;
                        break;
                    }

                    var d = (double)(input[j]!.Value - mean);
                    sumSq += d * d;
                }

                if (!valid)
                {
                    continue;
                }

                var std = (decimal)Math.Sqrt(sumSq / period);
                upper[i] = mean + multiplier * std;
                lower[i] = mean - multiplier * std;
            }

            return new BollingerBandSeries
            {
                Basis = new NumericSeries(candles, basis, "BB.Basis"),
                Upper = new NumericSeries(candles, upper, "BB.Upper"),
                Lower = new NumericSeries(candles, lower, "BB.Lower")
            };
        });
    }

    public static NumericSeries ATR(
        this Candles candles,
        int period = 14)
    {
        var key = $"ATR|{period}";
        return IndicatorCache.GetOrCreate(candles, key, () =>
        {
            var tr = TrueRange(candles);
            var atr = Wilder(tr, period);
            return new NumericSeries(candles, atr, $"ATR({period})");
        });
    }

    public static AdxSeries ADX(
        this Candles candles,
        int period = 14)
    {
        var key = $"ADX|{period}";
        return IndicatorCache.GetOrCreate(candles, key, () =>
        {
            var count = candles.Count;
            var tr = TrueRange(candles);
            var plusDm = new decimal?[count];
            var minusDm = new decimal?[count];

            for (var i = 1; i < count; i++)
            {
                var up = candles[i].High - candles[i - 1].High;
                var down = candles[i - 1].Low - candles[i].Low;
                plusDm[i] = up > down && up > 0m ? up : 0m;
                minusDm[i] = down > up && down > 0m ? down : 0m;
            }

            var atr = Wilder(tr, period);
            var smPlus = Wilder(plusDm, period);
            var smMinus = Wilder(minusDm, period);
            var plusDi = new decimal?[count];
            var minusDi = new decimal?[count];
            var dx = new decimal?[count];

            for (var i = 0; i < count; i++)
            {
                if (!atr[i].HasValue || atr[i] == 0m ||
                    !smPlus[i].HasValue || !smMinus[i].HasValue)
                {
                    continue;
                }

                plusDi[i] = 100m * smPlus[i]!.Value / atr[i]!.Value;
                minusDi[i] = 100m * smMinus[i]!.Value / atr[i]!.Value;

                var denom = plusDi[i]!.Value + minusDi[i]!.Value;
                dx[i] = denom == 0m
                    ? 0m
                    : 100m * Math.Abs(plusDi[i]!.Value - minusDi[i]!.Value) / denom;
            }

            var adx = Wilder(dx, period);

            return new AdxSeries
            {
                Adx = new NumericSeries(candles, adx, $"ADX({period})"),
                PlusDi = new NumericSeries(candles, plusDi, "+DI"),
                MinusDi = new NumericSeries(candles, minusDi, "-DI")
            };
        });
    }

    public static SuperTrendSeries SuperTrend(
        this Candles candles,
        int atrPeriod = 10,
        decimal factor = 3m)
    {
        var key = $"ST|{atrPeriod}|{factor}";
        return IndicatorCache.GetOrCreate(candles, key, () =>
        {
            var atr = candles.ATR(atrPeriod).Values;
            var count = candles.Count;
            var value = new decimal?[count];
            var direction = new SuperTrendDirection?[count];
            var finalUpper = new decimal?[count];
            var finalLower = new decimal?[count];

            for (var i = 0; i < count; i++)
            {
                if (!atr[i].HasValue)
                {
                    continue;
                }

                var hl2 = (candles[i].High + candles[i].Low) / 2m;
                var upper = hl2 + factor * atr[i]!.Value;
                var lower = hl2 - factor * atr[i]!.Value;

                if (i == 0 || !finalUpper[i - 1].HasValue || !finalLower[i - 1].HasValue)
                {
                    finalUpper[i] = upper;
                    finalLower[i] = lower;
                    direction[i] = candles[i].Close >= hl2
                        ? SuperTrendDirection.Bullish
                        : SuperTrendDirection.Bearish;
                }
                else
                {
                    finalUpper[i] =
                        upper < finalUpper[i - 1]!.Value ||
                        candles[i - 1].Close > finalUpper[i - 1]!.Value
                            ? upper
                            : finalUpper[i - 1];

                    finalLower[i] =
                        lower > finalLower[i - 1]!.Value ||
                        candles[i - 1].Close < finalLower[i - 1]!.Value
                            ? lower
                            : finalLower[i - 1];

                    var prevDirection = direction[i - 1] ?? SuperTrendDirection.Bearish;

                    if (prevDirection == SuperTrendDirection.Bearish &&
                        candles[i].Close > finalUpper[i]!.Value)
                    {
                        direction[i] = SuperTrendDirection.Bullish;
                    }
                    else if (prevDirection == SuperTrendDirection.Bullish &&
                             candles[i].Close < finalLower[i]!.Value)
                    {
                        direction[i] = SuperTrendDirection.Bearish;
                    }
                    else
                    {
                        direction[i] = prevDirection;
                    }
                }

                value[i] = direction[i] == SuperTrendDirection.Bullish
                    ? finalLower[i]
                    : finalUpper[i];
            }

            var turnedBullish = new bool[count];
            var turnedBearish = new bool[count];

            for (var i = 1; i < count; i++)
            {
                if (direction[i - 1] == SuperTrendDirection.Bearish &&
                    direction[i] == SuperTrendDirection.Bullish)
                {
                    turnedBullish[i] = true;
                }
                else if (direction[i - 1] == SuperTrendDirection.Bullish &&
                         direction[i] == SuperTrendDirection.Bearish)
                {
                    turnedBearish[i] = true;
                }
            }

            return new SuperTrendSeries
            {
                Value = new NumericSeries(candles, value, $"SuperTrend({atrPeriod},{factor})"),
                Direction = direction,
                TurnedBullish = turnedBullish,
                TurnedBearish = turnedBearish
            };
        });
    }

    public static NumericSeries VWAP(
        this Candles candles)
    {
        var key = $"VWAP";
        return IndicatorCache.GetOrCreate(candles, key, () =>
        {
            var output = new decimal?[candles.Count];
            decimal cumulativePv = 0m;
            decimal cumulativeVolume = 0m;
            DateOnly? session = null;

            for (var i = 0; i < candles.Count; i++)
            {
                var currentSession = DateOnly.FromDateTime(candles[i].OpenTime.DateTime);

                if (session != currentSession)
                {
                    session = currentSession;
                    cumulativePv = 0m;
                    cumulativeVolume = 0m;
                }

                var typical = (candles[i].High + candles[i].Low + candles[i].Close) / 3m;
                cumulativePv += typical * candles[i].Volume;
                cumulativeVolume += candles[i].Volume;

                output[i] = cumulativeVolume == 0m
                    ? null
                    : cumulativePv / cumulativeVolume;
            }

            return new NumericSeries(candles, output, "VWAP");
        });
    }

    public static NumericSeries OBV(
        this Candles candles)
    {
        var key = $"OBV";
        return IndicatorCache.GetOrCreate(candles, key, () =>
        {
            var output = new decimal?[candles.Count];

            if (candles.Count == 0)
            {
                return new NumericSeries(candles, output, "OBV");
            }

            decimal obv = 0m;
            output[0] = obv;

            for (var i = 1; i < candles.Count; i++)
            {
                if (candles[i].Close > candles[i - 1].Close)
                {
                    obv += candles[i].Volume;
                }
                else if (candles[i].Close < candles[i - 1].Close)
                {
                    obv -= candles[i].Volume;
                }

                output[i] = obv;
            }

            return new NumericSeries(candles, output, "OBV");
        });
    }

    public static StochasticSeries Stochastic(
        this Candles candles,
        int kPeriod = 14,
        int dPeriod = 3)
    {
        var key = $"STOCH|{kPeriod}|{dPeriod}";
        return IndicatorCache.GetOrCreate(candles, key, () =>
        {
            ValidatePeriod(kPeriod);
            ValidatePeriod(dPeriod);
            var k = new decimal?[candles.Count];

            for (var i = kPeriod - 1; i < candles.Count; i++)
            {
                var hh = decimal.MinValue;
                var ll = decimal.MaxValue;

                for (var j = i - kPeriod + 1; j <= i; j++)
                {
                    hh = Math.Max(hh, candles[j].High);
                    ll = Math.Min(ll, candles[j].Low);
                }

                var range = hh - ll;
                k[i] = range == 0m
                    ? null
                    : 100m * (candles[i].Close - ll) / range;
            }

            var d = SmaNullable(k, dPeriod);

            return new StochasticSeries
            {
                K = new NumericSeries(candles, k, "%K"),
                D = new NumericSeries(candles, d, "%D")
            };
        });
    }

    public static NumericSeries CCI(
        this Candles candles,
        int period = 20)
    {
        var key = $"CCI|{period}";
        return IndicatorCache.GetOrCreate(candles, key, () =>
        {
            var typical = candles
                .Select(c => (decimal?)((c.High + c.Low + c.Close) / 3m))
                .ToArray();

            var sma = SmaNullable(typical, period);
            var output = new decimal?[candles.Count];

            for (var i = period - 1; i < candles.Count; i++)
            {
                if (!sma[i].HasValue)
                {
                    continue;
                }

                var mean = sma[i]!.Value;
                decimal deviation = 0m;

                for (var j = i - period + 1; j <= i; j++)
                {
                    deviation += Math.Abs(typical[j]!.Value - mean);
                }

                deviation /= period;

                output[i] = deviation == 0m
                    ? 0m
                    : (typical[i]!.Value - mean) / (0.015m * deviation);
            }

            return new NumericSeries(candles, output, $"CCI({period})");
        });
    }

    public static NumericSeries VolumeSMA(
        this Candles candles,
        int period = 20)
        => candles.SMA(period, SourceField.Volume);

    public static NumericSeries High52W(
        this Candles candles,
        int bars = 252)
        => RollingExtreme(candles, bars, High: true, name: "High52W");

    public static NumericSeries Low52W(
        this Candles candles,
        int bars = 252)
        => RollingExtreme(candles, bars, High: false, name: "Low52W");

    public static NumericSeries ROC(
        this Candles candles,
        int period = 12,
        SourceField source = SourceField.Close)
    {
        var key = $"ROC|{period}|{source}";
        return IndicatorCache.GetOrCreate(candles, key, () =>
        {
            var input = Select(candles, source);
            var output = new decimal?[input.Length];

            for (var i = period; i < input.Length; i++)
            {
                if (!input[i].HasValue || !input[i - period].HasValue ||
                    input[i - period] == 0m)
                {
                    continue;
                }

                output[i] =
                    100m * (input[i]!.Value - input[i - period]!.Value) /
                    input[i - period]!.Value;
            }

            return new NumericSeries(candles, output, $"ROC({period})");
        });
    }

    public static NumericSeries Momentum(
        this Candles candles,
        int period = 10,
        SourceField source = SourceField.Close)
    {
        var key = $"MOM|{period}|{source}";
        return IndicatorCache.GetOrCreate(candles, key, () =>
        {
            var input = Select(candles, source);
            var output = new decimal?[input.Length];

            for (var i = period; i < input.Length; i++)
            {
                if (input[i].HasValue && input[i - period].HasValue)
                {
                    output[i] = input[i]!.Value - input[i - period]!.Value;
                }
            }

            return new NumericSeries(candles, output, $"Momentum({period})");
        });
    }

    public static NumericSeries TRIX(
        this Candles candles,
        int period = 15,
        SourceField source = SourceField.Close)
    {
        var key = $"TRIX|{period}|{source}";
        return IndicatorCache.GetOrCreate(candles, key, () =>
        {
            var input = Select(candles, source);
            var ema1 = Ema(input, period);
            var ema2 = Ema(ema1, period);
            var ema3 = Ema(ema2, period);
            var output = new decimal?[input.Length];

            for (var i = 1; i < input.Length; i++)
            {
                if (!ema3[i].HasValue || !ema3[i - 1].HasValue ||
                    ema3[i - 1] == 0m)
                {
                    continue;
                }

                output[i] =
                    100m * (ema3[i]!.Value - ema3[i - 1]!.Value) /
                    ema3[i - 1]!.Value;
            }

            return new NumericSeries(candles, output, $"TRIX({period})");
        });
    }

    public static KeltnerSeries Keltner(
        this Candles candles,
        int emaPeriod = 20,
        int atrPeriod = 10,
        decimal multiplier = 2m)
    {
        var key = $"KELTNER|{emaPeriod}|{atrPeriod}|{multiplier}";
        return IndicatorCache.GetOrCreate(candles, key, () =>
        {
            var basis = candles.EMA(emaPeriod).Values;
            var atr = candles.ATR(atrPeriod).Values;
            var upper = new decimal?[candles.Count];
            var lower = new decimal?[candles.Count];

            for (var i = 0; i < candles.Count; i++)
            {
                if (basis[i].HasValue && atr[i].HasValue)
                {
                    upper[i] = basis[i]!.Value + multiplier * atr[i]!.Value;
                    lower[i] = basis[i]!.Value - multiplier * atr[i]!.Value;
                }
            }

            return new KeltnerSeries
            {
                Basis = new NumericSeries(candles, basis, "Keltner.Basis"),
                Upper = new NumericSeries(candles, upper, "Keltner.Upper"),
                Lower = new NumericSeries(candles, lower, "Keltner.Lower")
            };
        });
    }

    public static DonchianSeries Donchian(
        this Candles candles,
        int period = 20)
    {
        var key = $"DONCHIAN|{period}";
        return IndicatorCache.GetOrCreate(candles, key, () =>
        {
            ValidatePeriod(period);
            var upper = new decimal?[candles.Count];
            var lower = new decimal?[candles.Count];
            var middle = new decimal?[candles.Count];

            for (var i = period - 1; i < candles.Count; i++)
            {
                var hh = decimal.MinValue;
                var ll = decimal.MaxValue;

                for (var j = i - period + 1; j <= i; j++)
                {
                    hh = Math.Max(hh, candles[j].High);
                    ll = Math.Min(ll, candles[j].Low);
                }

                upper[i] = hh;
                lower[i] = ll;
                middle[i] = (hh + ll) / 2m;
            }

            return new DonchianSeries
            {
                Upper = new NumericSeries(candles, upper, "Donchian.Upper"),
                Middle = new NumericSeries(candles, middle, "Donchian.Middle"),
                Lower = new NumericSeries(candles, lower, "Donchian.Lower")
            };
        });
    }

    /// <summary>Cumulative money-flow volume. A zero-range candle contributes zero.</summary>
    public static NumericSeries ADL(this Candles candles)
        => IndicatorCache.GetOrCreate(candles, "ADL", () =>
        {
            var output = new decimal?[candles.Count];
            decimal total = 0m;
            for (int i = 0; i < candles.Count; i++)
            {
                var c = candles[i];
                decimal range = c.High - c.Low;
                if (range != 0m)
                    total += ((c.Close - c.Low) - (c.High - c.Close)) / range * c.Volume;
                output[i] = total;
            }
            return new NumericSeries(candles, output, "ADL");
        });

    /// <summary>
    /// Rolling money flow classified by typical-price changes. First output is at index period.
    /// Equal typical prices contribute neither flow; no directional flow produces null.
    /// </summary>
    public static NumericSeries MFI(this Candles candles, int period = 14)
        => IndicatorCache.GetOrCreate(candles, $"MFI|{period}", () =>
        {
            ValidatePeriod(period);
            var output = new decimal?[candles.Count];
            var positive = new decimal[candles.Count];
            var negative = new decimal[candles.Count];
            decimal positiveSum = 0m, negativeSum = 0m;
            decimal Typical(int i) => (candles[i].High + candles[i].Low + candles[i].Close) / 3m;
            for (int i = 1; i < candles.Count; i++)
            {
                decimal typical = Typical(i), previous = Typical(i - 1);
                decimal flow = typical * candles[i].Volume;
                if (typical > previous) positive[i] = flow;
                else if (typical < previous) negative[i] = flow;
                positiveSum += positive[i];
                negativeSum += negative[i];
                if (i > period)
                {
                    positiveSum -= positive[i - period];
                    negativeSum -= negative[i - period];
                }
                if (i >= period)
                    output[i] = negativeSum == 0m ? positiveSum == 0m ? null : 100m
                        : 100m - 100m / (1m + positiveSum / negativeSum);
            }
            return new NumericSeries(candles, output, $"MFI({period})");
        });

    /// <summary>
    /// Each D/W/M candle receives levels from its preceding completed source candle.
    /// First output is null. Intraday callers must supply completed positional source candles
    /// and project the levels to their session; partial intraday history is not aggregated here.
    /// </summary>
    public static CamarillaSeries Camarilla(this Candles candles)
        => IndicatorCache.GetOrCreate(candles, "CAMARILLA", () =>
        {
            if (candles.Timeframe != Timeframe.Day && candles.Timeframe != Timeframe.Week
                && candles.Timeframe != Timeframe.Month)
                throw new ArgumentException("Camarilla requires completed 1D, 1W or 1M source candles.", nameof(candles));
            var values = Enumerable.Range(0, 8).Select(_ => new decimal?[candles.Count]).ToArray();
            for (int i = 1; i < candles.Count; i++)
            {
                var source = candles[i - 1];
                var p = CamarillaPivots.Calculate(source.High, source.Low, source.Close);
                values[0][i] = p.R4; values[1][i] = p.R3;
                values[2][i] = p.R2; values[3][i] = p.R1;
                values[4][i] = p.S1; values[5][i] = p.S2;
                values[6][i] = p.S3; values[7][i] = p.S4;
            }
            NumericSeries Line(int index, string name) => new(candles, values[index], $"Camarilla.{name}");
            return new CamarillaSeries
            {
                R4 = Line(0, "R4"), R3 = Line(1, "R3"), R2 = Line(2, "R2"), R1 = Line(3, "R1"),
                S1 = Line(4, "S1"), S2 = Line(5, "S2"), S3 = Line(6, "S3"), S4 = Line(7, "S4")
            };
        });

    private static NumericSeries RollingExtreme(
        Candles candles,
        int period,
        bool High,
        string name)
    {
        var key = $"{name}|{period}";
        return IndicatorCache.GetOrCreate(candles, key, () =>
        {
            ValidatePeriod(period);
            var output = new decimal?[candles.Count];

            for (var i = period - 1; i < candles.Count; i++)
            {
                var extreme = High ? decimal.MinValue : decimal.MaxValue;

                for (var j = i - period + 1; j <= i; j++)
                {
                    var value = High ? candles[j].High : candles[j].Low;
                    extreme = High ? Math.Max(extreme, value) : Math.Min(extreme, value);
                }

                output[i] = extreme;
            }

            return new NumericSeries(candles, output, name);
        });
    }

    private static RsiDivergenceType?[] DetectRsiDivergence(
        Candles candles,
        NumericSeries rsi,
        int pivotLookback,
        int maxBarsBetweenPivots)
    {
        var count = candles.Count;
        var output = new RsiDivergenceType?[count];

        int? lastPivotHighIdx = null;
        int? lastPivotLowIdx = null;

        for (var i = pivotLookback; i < count - pivotLookback; i++)
        {
            if (IsPivotHigh(candles, i, pivotLookback) && rsi.Values[i].HasValue)
            {
                if (lastPivotHighIdx.HasValue &&
                    i - lastPivotHighIdx.Value <= maxBarsBetweenPivots &&
                    rsi.Values[lastPivotHighIdx.Value].HasValue)
                {
                    var priceHigher =
                        candles[i].High > candles[lastPivotHighIdx.Value].High;
                    var rsiLower =
                        rsi.Values[i]!.Value <
                        rsi.Values[lastPivotHighIdx.Value]!.Value;

                    if (priceHigher && rsiLower)
                    {
                        output[i + pivotLookback] = RsiDivergenceType.Bearish;
                    }
                }

                lastPivotHighIdx = i;
            }

            if (IsPivotLow(candles, i, pivotLookback) && rsi.Values[i].HasValue)
            {
                if (lastPivotLowIdx.HasValue &&
                    i - lastPivotLowIdx.Value <= maxBarsBetweenPivots &&
                    rsi.Values[lastPivotLowIdx.Value].HasValue)
                {
                    var priceLower =
                        candles[i].Low < candles[lastPivotLowIdx.Value].Low;
                    var rsiHigher =
                        rsi.Values[i]!.Value >
                        rsi.Values[lastPivotLowIdx.Value]!.Value;

                    if (priceLower && rsiHigher)
                    {
                        output[i + pivotLookback] = RsiDivergenceType.Bullish;
                    }
                }

                lastPivotLowIdx = i;
            }
        }

        return output;
    }

    private static bool IsPivotHigh(Candles candles, int index, int lookback)
    {
        var high = candles[index].High;

        for (var j = index - lookback; j <= index + lookback; j++)
        {
            if (j == index)
            {
                continue;
            }

            if (candles[j].High >= high)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsPivotLow(Candles candles, int index, int lookback)
    {
        var low = candles[index].Low;

        for (var j = index - lookback; j <= index + lookback; j++)
        {
            if (j == index)
            {
                continue;
            }

            if (candles[j].Low <= low)
            {
                return false;
            }
        }

        return true;
    }

    internal static NumericSeries ApplySourceIndicator(
        this NumericSeries source,
        IndicatorSpec spec,
        string cacheKey)
        => IndicatorCache.GetOrCreate(source.Candles, cacheKey, () =>
        {
            var input = source.Values;
            var p1 = spec.Period1 > 0 ? spec.Period1 : 14;
            decimal?[] output;
            string name;

            switch (spec.Kind)
            {
                case IndicatorType.SMA:
                    output = SmaNullable(input, p1); name = $"SMA({p1})"; break;
                case IndicatorType.EMA:
                    output = Ema(input, p1); name = $"EMA({p1})"; break;
                case IndicatorType.DEMA:
                {
                    var first = Ema(input, p1); var second = Ema(first, p1);
                    output = new decimal?[input.Length];
                    for (var i = 0; i < output.Length; i++)
                        if (first[i].HasValue && second[i].HasValue)
                            output[i] = (2m * first[i]!.Value) - second[i]!.Value;
                    name = $"DEMA({p1})"; break;
                }
                case IndicatorType.TEMA:
                {
                    var first = Ema(input, p1); var second = Ema(first, p1); var third = Ema(second, p1);
                    output = new decimal?[input.Length];
                    for (var i = 0; i < output.Length; i++)
                        if (first[i].HasValue && second[i].HasValue && third[i].HasValue)
                            output[i] = (3m * first[i]!.Value) - (3m * second[i]!.Value) + third[i]!.Value;
                    name = $"TEMA({p1})"; break;
                }
                case IndicatorType.WMA:
                    output = Wma(input, p1); name = $"WMA({p1})"; break;
                case IndicatorType.HMA:
                {
                    var half = Math.Max(1, (int)Math.Round(p1 / 2m, MidpointRounding.AwayFromZero));
                    var root = Math.Max(1, (int)Math.Round(Math.Sqrt(p1)));
                    var shortWma = Wma(input, half); var fullWma = Wma(input, p1);
                    var difference = new decimal?[input.Length];
                    for (var i = 0; i < difference.Length; i++)
                        if (shortWma[i].HasValue && fullWma[i].HasValue)
                            difference[i] = (2m * shortWma[i]!.Value) - fullWma[i]!.Value;
                    output = Wma(difference, root); name = $"HMA({p1})"; break;
                }
                case IndicatorType.RSI:
                {
                    var rsi = Rsi(input, p1);
                    if (spec.Component == IndicatorComponent.RsiSmooth)
                    {
                        var smoothPeriod = spec.Period2 > 0 ? spec.Period2 : 14;
                        output = spec.Smoothing switch
                        {
                            MovingAverageType.SMA => SmaNullable(rsi, smoothPeriod),
                            MovingAverageType.EMA => Ema(rsi, smoothPeriod),
                            MovingAverageType.WMA => Wma(rsi, smoothPeriod),
                            _ => throw new InvalidOperationException("RsiSmooth requires SMA, EMA or WMA smoothing.")
                        };
                        name = $"RSI.Smooth({smoothPeriod})";
                    }
                    else if (spec.Component == IndicatorComponent.RsiDivergence)
                    {
                        var series = new NumericSeries(source.Candles, rsi, $"RSI({p1})");
                        var divergence = DetectRsiDivergence(source.Candles, series, 5, 60);
                        output = new decimal?[input.Length];
                        for (var i = 0; i < output.Length; i++)
                            output[i] = !rsi[i].HasValue ? null : divergence[i] switch
                            {
                                RsiDivergenceType.Bullish => 1m,
                                RsiDivergenceType.Bearish => -1m,
                                _ => 0m
                            };
                        name = "RSI.Divergence";
                    }
                    else { output = rsi; name = $"RSI({p1})"; }
                    break;
                }
                case IndicatorType.MACD:
                {
                    var fast = Ema(input, spec.Period1 > 0 ? spec.Period1 : 12);
                    var slow = Ema(input, spec.Period2 > 0 ? spec.Period2 : 26);
                    var macd = new decimal?[input.Length];
                    for (var i = 0; i < macd.Length; i++)
                        if (fast[i].HasValue && slow[i].HasValue) macd[i] = fast[i]!.Value - slow[i]!.Value;
                    var signal = Ema(macd, spec.Period3 > 0 ? spec.Period3 : 9);
                    output = new decimal?[input.Length];
                    for (var i = 0; i < output.Length; i++)
                        output[i] = spec.Component switch
                        {
                            IndicatorComponent.Signal => signal[i],
                            IndicatorComponent.Histogram when macd[i].HasValue && signal[i].HasValue => macd[i]!.Value - signal[i]!.Value,
                            IndicatorComponent.Histogram => null,
                            _ => macd[i]
                        };
                    name = $"MACD.{spec.Component}"; break;
                }
                case IndicatorType.BollingerBands:
                {
                    var period = spec.Period1 > 0 ? spec.Period1 : 20;
                    var multiplier = spec.Decimal1 == 0m ? 2m : spec.Decimal1;
                    var basis = SmaNullable(input, period);
                    output = new decimal?[input.Length];
                    for (var i = period - 1; i < input.Length; i++)
                    {
                        if (!basis[i].HasValue) continue;
                        var sum = 0d; var valid = true;
                        for (var j = i - period + 1; j <= i; j++)
                        { if (!input[j].HasValue) { valid = false; break; } var d = (double)(input[j]!.Value - basis[i]!.Value); sum += d * d; }
                        if (!valid) continue;
                        var deviation = (decimal)Math.Sqrt(sum / period);
                        output[i] = spec.Component switch
                        {
                            IndicatorComponent.Upper => basis[i]!.Value + (multiplier * deviation),
                            IndicatorComponent.Lower => basis[i]!.Value - (multiplier * deviation),
                            _ => basis[i]
                        };
                    }
                    name = $"BB.{spec.Component}"; break;
                }
                case IndicatorType.ROC:
                case IndicatorType.Momentum:
                {
                    output = new decimal?[input.Length];
                    for (var i = p1; i < input.Length; i++)
                    {
                        if (!input[i].HasValue || !input[i - p1].HasValue) continue;
                        if (spec.Kind == IndicatorType.ROC)
                        { if (input[i - p1] == 0m) continue; output[i] = 100m * (input[i]!.Value - input[i - p1]!.Value) / input[i - p1]!.Value; }
                        else output[i] = input[i]!.Value - input[i - p1]!.Value;
                    }
                    name = $"{spec.Kind}({p1})"; break;
                }
                case IndicatorType.TRIX:
                {
                    var first = Ema(input, p1); var second = Ema(first, p1); var third = Ema(second, p1);
                    output = new decimal?[input.Length];
                    for (var i = 1; i < output.Length; i++)
                        if (third[i].HasValue && third[i - 1].HasValue && third[i - 1] != 0m)
                            output[i] = 100m * (third[i]!.Value - third[i - 1]!.Value) / third[i - 1]!.Value;
                    name = $"TRIX({p1})"; break;
                }
                default:
                    throw new InvalidOperationException($"{spec.Kind} does not accept an input operand.");
            }

            return new NumericSeries(source.Candles, output, name);
        });

    private static decimal?[] Select(IReadOnlyList<Candle> candles, SourceField source)
    {
        var output = new decimal?[candles.Count];

        for (var i = 0; i < candles.Count; i++)
        {
            output[i] = source switch
            {
                SourceField.Open => candles[i].Open,
                SourceField.High => candles[i].High,
                SourceField.Low => candles[i].Low,
                SourceField.Close => candles[i].Close,
                SourceField.Volume => candles[i].Volume,
                SourceField.OpenInterest => candles[i].OpenInterest,
                _ => throw new ArgumentOutOfRangeException(nameof(source))
            };
        }

        return output;
    }

    private static void ValidatePeriod(int period)
    {
        if (period <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(period), "Period must be greater than zero.");
        }
    }

    private static decimal?[] SmaNullable(decimal?[] input, int period)
    {
        ValidatePeriod(period);
        var output = new decimal?[input.Length];

        for (var i = period - 1; i < input.Length; i++)
        {
            decimal sum = 0m;
            var valid = true;

            for (var j = i - period + 1; j <= i; j++)
            {
                if (!input[j].HasValue)
                {
                    valid = false;
                    break;
                }

                sum += input[j]!.Value;
            }

            if (valid)
            {
                output[i] = sum / period;
            }
        }

        return output;
    }

    private static decimal?[] Ema(decimal?[] input, int period)
    {
        ValidatePeriod(period);
        var output = new decimal?[input.Length];
        var alpha = 2m / (period + 1m);

        decimal sum = 0m;
        var seedCount = 0;
        decimal? previous = null;

        for (var i = 0; i < input.Length; i++)
        {
            if (!input[i].HasValue)
            {
                continue;
            }

            if (!previous.HasValue)
            {
                sum += input[i]!.Value;
                seedCount++;

                if (seedCount == period)
                {
                    previous = sum / period;
                    output[i] = previous;
                }

                continue;
            }

            previous = input[i]!.Value * alpha + previous.Value * (1m - alpha);
            output[i] = previous;
        }

        return output;
    }

    private static decimal?[] Wma(decimal?[] input, int period)
    {
        ValidatePeriod(period);
        var output = new decimal?[input.Length];
        var denominator = period * (period + 1) / 2m;

        for (var i = period - 1; i < input.Length; i++)
        {
            decimal weighted = 0m;
            var valid = true;
            var weight = 1;

            for (var j = i - period + 1; j <= i; j++)
            {
                if (!input[j].HasValue)
                {
                    valid = false;
                    break;
                }

                weighted += input[j]!.Value * weight;
                weight++;
            }

            if (valid)
            {
                output[i] = weighted / denominator;
            }
        }

        return output;
    }

    private static decimal?[] Rsi(decimal?[] input, int period)
    {
        ValidatePeriod(period);
        var output = new decimal?[input.Length];

        if (input.Length <= period)
        {
            return output;
        }

        decimal gainSum = 0m;
        decimal lossSum = 0m;
        var changes = 0;

        for (var i = 1; i < input.Length; i++)
        {
            if (!input[i].HasValue || !input[i - 1].HasValue)
            {
                continue;
            }

            var change = input[i]!.Value - input[i - 1]!.Value;
            gainSum += Math.Max(change, 0m);
            lossSum += Math.Max(-change, 0m);
            changes++;

            if (changes == period)
            {
                var avgGain = gainSum / period;
                var avgLoss = lossSum / period;
                output[i] = ToRsi(avgGain, avgLoss);

                for (var k = i + 1; k < input.Length; k++)
                {
                    if (!input[k].HasValue || !input[k - 1].HasValue)
                    {
                        continue;
                    }

                    change = input[k]!.Value - input[k - 1]!.Value;
                    var gain = Math.Max(change, 0m);
                    var loss = Math.Max(-change, 0m);
                    avgGain = ((avgGain * (period - 1)) + gain) / period;
                    avgLoss = ((avgLoss * (period - 1)) + loss) / period;
                    output[k] = ToRsi(avgGain, avgLoss);
                }

                break;
            }
        }

        return output;
    }

    private static decimal ToRsi(decimal avgGain, decimal avgLoss)
    {
        if (avgLoss == 0m)
        {
            return avgGain == 0m ? 50m : 100m;
        }

        var rs = avgGain / avgLoss;
        return 100m - (100m / (1m + rs));
    }

    private static decimal?[] Wilder(decimal?[] input, int period)
    {
        ValidatePeriod(period);
        var output = new decimal?[input.Length];
        decimal sum = 0m;
        var count = 0;
        decimal? previous = null;

        for (var i = 0; i < input.Length; i++)
        {
            if (!input[i].HasValue)
            {
                continue;
            }

            if (!previous.HasValue)
            {
                sum += input[i]!.Value;
                count++;

                if (count == period)
                {
                    previous = sum / period;
                    output[i] = previous;
                }

                continue;
            }

            previous = ((previous.Value * (period - 1)) + input[i]!.Value) / period;
            output[i] = previous;
        }

        return output;
    }

    private static decimal?[] TrueRange(IReadOnlyList<Candle> candles)
    {
        var output = new decimal?[candles.Count];

        for (var i = 0; i < candles.Count; i++)
        {
            if (i == 0)
            {
                output[i] = candles[i].High - candles[i].Low;
                continue;
            }

            var a = candles[i].High - candles[i].Low;
            var b = Math.Abs(candles[i].High - candles[i - 1].Close);
            var c = Math.Abs(candles[i].Low - candles[i - 1].Close);
            output[i] = Math.Max(a, Math.Max(b, c));
        }

        return output;
    }
}
