using Stage1Scanner.Core.Indicators;
using Stage1Scanner.Core.Pivots;
using Core = Stage1Scanner.Core.Market;
using Rules = Stage1Scanner.Core.Rules;
using AlgoTrading.Aggregation;
using AlgoTrading.Backtest.Runtime;
using AlgoTrading.Models;
using Xunit;

namespace AlgoTrading.Backtest.Tests;

public sealed class DailyPivotIntegrationTests
{
    [Fact]
    public void NestedIndicator_PreservesWarmupAndUsesActualPeriod()
    {
        var tf = Timeframe.Parse("D");
        var series = TestSeries.FromCloses(tf, Utc(2024, 1, 1, 3, 45), 1, 2, 3, 4, 5, 6);
        var provider = new IndicatorProvider(new TimeframeAccessor(1, series, new TimeframeCache(series)));
        var inner = new IndicatorExpression
        {
            Indicator = IndicatorKind.Ema,
            Source = new FieldExpression { Field = Field.Close },
            Params = new Dictionary<string, decimal> { ["period"] = 2 }
        };
        var outer = new IndicatorExpression
        {
            Indicator = IndicatorKind.Sma, Source = inner,
            Params = new Dictionary<string, decimal> { ["period"] = 3 }
        };
        var result = provider.Resolve(outer, tf);
        Assert.All(result.Outputs[0].Take(3), value => Assert.Null(value));
        for (int i = 3; i < 6; i++)
            Assert.InRange(Math.Abs(result.Outputs[0][i]!.Value - (i - 0.5m)), 0m, 0.00000000000000000001m);
        Assert.Equal(3, result.FirstValidIndex);
        Assert.Same(result, provider.Resolve(outer, tf));
    }

    [Fact]
    public void Camarilla_UsesPreviousDailySourceForIntradayProjection()
    {
        var intraday = CreateIntradaySeries();
        var provider = new IndicatorProvider(new TimeframeAccessor(1, intraday,
            new TimeframeCache(intraday, CreateDailySeries())));
        var expression = new IndicatorExpression
        {
            Indicator = IndicatorKind.PivotPointsCamarilla,
            Timeframe = Timeframe.Parse("15m"), Output = "R4"
        };
        var result = provider.Resolve(expression, Timeframe.Parse("15m"));
        Assert.Equal(new decimal?[] { 110.5m, 110.5m, 120.5m, 120.5m }, result.Outputs[0]);
        Assert.Equal(new[] { "R4", "R3", "R2", "R1", "S1", "S2", "S3", "S4" }, result.OutputNames);
    }

    [Fact]
    public void ClassicPivot_UsesDailyCandlesAndProjectsOntoIndicatorTimeframe()
    {
        var intraday = CreateIntradaySeries();
        var daily = CreateDailySeries();
        var cache = new TimeframeCache(intraday, daily);
        var accessor = new TimeframeAccessor(intraday.InstrumentToken, intraday, cache);
        var provider = new IndicatorProvider(accessor);
        var expression = new IndicatorExpression
        {
            Indicator = IndicatorKind.PivotPointsClassic,
            Timeframe = Timeframe.Parse("15m"),
            Output = "PP"
        };

        var result = provider.Resolve(expression, Timeframe.Parse("15m"));
        int ppIndex = result.IndexOfOutput("PP");

        Assert.Equal(4, result.Outputs[ppIndex].Length);
        Assert.Equal(105m, result.Outputs[ppIndex][0]);
        Assert.Equal(105m, result.Outputs[ppIndex][1]);
        Assert.Equal(115m, result.Outputs[ppIndex][2]);
        Assert.Equal(115m, result.Outputs[ppIndex][3]);
    }

    [Fact]
    public void OrdinaryIndicator_UsesExplicitOrEvaluationTimeframe()
    {
        var intraday = CreateIntradaySeries();
        var daily = CreateDailySeries();
        var cache = new TimeframeCache(intraday, daily);
        var accessor = new TimeframeAccessor(intraday.InstrumentToken, intraday, cache);
        var provider = new IndicatorProvider(accessor);

        var evaluationExpression = CreateSmaExpression(null);
        var dailyExpression = CreateSmaExpression(Timeframe.Parse("D"));

        var evaluationResult = provider.Resolve(evaluationExpression, Timeframe.Parse("15m"));
        var dailyResult = provider.Resolve(dailyExpression, Timeframe.Parse("15m"));

        Assert.Equal(intraday.Count, evaluationResult.Outputs[0].Length);
        Assert.Equal(daily.Count, dailyResult.Outputs[0].Length);
    }

    [Fact]
    public void AllRegisteredOutputsMatchStage1AndRemainCausal()
    {
        const int n = 300;
        var start = new DateTimeOffset(2024, 1, 1, 9, 15, 0, TimeSpan.FromMinutes(330));
        var close = Enumerable.Range(0, n).Select(i => 100m + i % 17 * 2m - i % 7 * 3m).ToArray();
        var high = close.Select((v, i) => v + 2m + i % 3).ToArray();
        var low = close.Select((v, i) => v - 2m - i % 2).ToArray();
        var volume = Enumerable.Range(0, n).Select(i => i % 11 == 0 ? 0m : 100m + i).ToArray();
        var timestamps = Enumerable.Range(0, n).Select(i => start.AddDays(i / 25).AddMinutes(i % 25 * 15)).ToArray();
        var tf = Core.Timeframe.Minutes(15);
        var bars = new Core.Candles(tf, Enumerable.Range(0, n).Select(i =>
            Core.Candle.Create(timestamps[i], tf, close[i], high[i], low[i], close[i], volume[i])));
        var daily = new Core.Candles(Core.Timeframe.Day, bars.GroupBy(c => c.OpenTime.Date)
            .Select(g => Core.Candle.Create(g.First().OpenTime, Core.Timeframe.Day,
                g.First().Open, g.Max(c => c.High), g.Min(c => c.Low), g.Last().Close, g.Sum(c => c.Volume))));
        var instrument = new Core.Instruments(1, "TEST");
        instrument.SetCandles(tf, bars);
        decimal?[] Values(Stage1Scanner.Core.Series.NumericSeries line) => line.Values;
        decimal?[][] Expected(IndicatorKind kind)
        {
            switch (kind)
            {
                case IndicatorKind.Sma: return new[] { Values(bars.SMA(20)) };
                case IndicatorKind.Ema: return new[] { Values(bars.EMA(20)) };
                case IndicatorKind.Dema: return new[] { Values(bars.DEMA(20)) };
                case IndicatorKind.Tema: return new[] { Values(bars.TEMA(20)) };
                case IndicatorKind.Wma: return new[] { Values(bars.WMA(20)) };
                case IndicatorKind.Hma: return new[] { Values(bars.HMA(20)) };
                case IndicatorKind.Rsi: return new[] { Values(bars.RSI().Value) };
                case IndicatorKind.Macd: var macd = bars.MACD(); return new[] { Values(macd.Macd), Values(macd.Signal), Values(macd.Histogram) };
                case IndicatorKind.BollingerBands: var bb = bars.BollingerBands(); return new[] { Values(bb.Upper), Values(bb.Basis), Values(bb.Lower) };
                case IndicatorKind.Atr: return new[] { Values(bars.ATR()) };
                case IndicatorKind.Adx: var adx = bars.ADX(); return new[] { Values(adx.Adx), Values(adx.PlusDi), Values(adx.MinusDi) };
                case IndicatorKind.Supertrend: var st = bars.SuperTrend(); return new[] { Values(st.Value), st.Direction.Select(x => x.HasValue ? (decimal?)(int)x.Value : null).ToArray() };
                case IndicatorKind.Vwap: return new[] { Values(bars.VWAP()) };
                case IndicatorKind.Obv: return new[] { Values(bars.OBV()) };
                case IndicatorKind.Adl: return new[] { Values(bars.ADL()) };
                case IndicatorKind.Stochastic: var sto = bars.Stochastic(); return new[] { Values(sto.K), Values(sto.D) };
                case IndicatorKind.Cci: return new[] { Values(bars.CCI()) };
                case IndicatorKind.VolumeSma: return new[] { Values(bars.VolumeSMA()) };
                case IndicatorKind.Roc: return new[] { Values(bars.ROC()) };
                case IndicatorKind.Momentum: return new[] { Values(bars.Momentum()) };
                case IndicatorKind.Trix:
                    // Builder's signal parameter is preserved as a composition of two Stage1 indicators.
                    var trix = new Rules.IndicatorOperand(new Rules.IndicatorSpec(Core.IndicatorType.TRIX, tf, Period1: 15));
                    var signal = new Rules.IndicatorOperand(new Rules.IndicatorSpec(Core.IndicatorType.EMA, tf, Period1: 9, Input: trix));
                    return new[] { bars.Select(c => signal.Resolve(new Rules.EvaluationContext(instrument, c.CloseTime)))
                        .Select(v => v.HasValue ? (decimal?)v.Value : null).ToArray() };
                case IndicatorKind.KeltnerChannel: var kc = bars.Keltner(); return new[] { Values(kc.Upper), Values(kc.Basis), Values(kc.Lower) };
                case IndicatorKind.DonchianChannel: var dc = bars.Donchian(); return new[] { Values(dc.Upper), Values(dc.Middle), Values(dc.Lower) };
                case IndicatorKind.Mfi: return new[] { Values(bars.MFI()) };
                case IndicatorKind.HighLow52W: return new[] { Values(bars.High52W(250)), Values(bars.Low52W(250)) };
                case IndicatorKind.PivotPointsClassic:
                case IndicatorKind.PivotPointsCamarilla:
                    var output = Enumerable.Range(0, kind == IndicatorKind.PivotPointsClassic ? 7 : 8).Select(_ => new decimal?[n]).ToArray();
                    var cam = daily.Camarilla();
                    var levels = new[] { cam.R4, cam.R3, cam.R2, cam.R1, cam.S1, cam.S2, cam.S3, cam.S4 };
                    for (int i = 25; i < n; i++)
                    {
                        int day = i / 25;
                        if (kind == IndicatorKind.PivotPointsCamarilla)
                            for (int k = 0; k < 8; k++) output[k][i] = levels[k][day];
                        else
                        {
                            var prev = daily[day - 1];
                            var p = PivotHelpers.Traditional(prev.High, prev.Low, prev.Close);
                            var values = new[] { p.P, p.R1, p.R2, p.R3, p.S1, p.S2, p.S3 };
                            for (int k = 0; k < 7; k++) output[k][i] = values[k];
                        }
                    }
                    return output;
                default: throw new InvalidOperationException(kind.ToString());
            }
        }
        foreach (var kind in Enum.GetValues<IndicatorKind>())
        {
            var actual = Dispatch(n, kind);
            var expected = Expected(kind);
            Assert.Equal(expected.Length, actual.Length);
            for (int k = 0; k < actual.Length; k++) Assert.Equal(expected[k], actual[k]);
            var prefix = Dispatch(67, kind);
            for (int k = 0; k < actual.Length; k++) Assert.Equal(actual[k].Take(67), prefix[k]);
        }
        TestSeries Series(Core.Candles candles, Timeframe timeframe, int count) => new()
        {
            Timeframe = timeframe,
            Open = candles.Take(count).Select(c => c.Open).ToArray(),
            High = candles.Take(count).Select(c => c.High).ToArray(),
            Low = candles.Take(count).Select(c => c.Low).ToArray(),
            Close = candles.Take(count).Select(c => c.Close).ToArray(),
            Volume = candles.Take(count).Select(c => c.Volume).ToArray(),
            OI = new decimal[count],
            DateTime = candles.Take(count).Select(c => c.OpenTime.UtcDateTime).ToArray()
        };
        decimal?[][] Dispatch(int count, IndicatorKind kind)
        {
            var timeframe = Timeframe.Parse("15m");
            var intraday = Series(bars, timeframe, count);
            var days = Series(daily, Timeframe.Parse("D"), (count + 24) / 25);
            var provider = new IndicatorProvider(new TimeframeAccessor(1, intraday, new TimeframeCache(intraday, days)));
            return provider.Resolve(new IndicatorExpression { Indicator = kind }, timeframe).Outputs;
        }
    }

    private static IndicatorExpression CreateSmaExpression(Timeframe? timeframe)
    {
        return new IndicatorExpression
        {
            Indicator = IndicatorKind.Sma,
            Timeframe = timeframe,
            Source = new FieldExpression { Field = Field.Close, Offset = 0 },
            Params = new Dictionary<string, decimal> { ["period"] = 2m }
        };
    }

    private static TestSeries CreateIntradaySeries()
    {
        return new TestSeries
        {
            Timeframe = Timeframe.Parse("15m"),
            Open = new decimal[] { 1m, 1m, 1m, 1m },
            High = new decimal[] { 2m, 2m, 2m, 2m },
            Low = new decimal[] { 0m, 0m, 0m, 0m },
            Close = new decimal[] { 1m, 1m, 1m, 1m },
            Volume = new decimal[] { 100m, 100m, 100m, 100m },
            OI = new decimal[] { 0m, 0m, 0m, 0m },
            DateTime = new DateTime[]
            {
                Utc(2024, 1, 3, 3, 45),
                Utc(2024, 1, 3, 4, 0),
                Utc(2024, 1, 4, 3, 45),
                Utc(2024, 1, 4, 4, 0)
            }
        };
    }

    private static TestSeries CreateDailySeries()
    {
        return new TestSeries
        {
            Timeframe = Timeframe.Parse("D"),
            Open = new decimal[] { 104m, 114m, 124m },
            High = new decimal[] { 110m, 120m, 130m },
            Low = new decimal[] { 100m, 110m, 120m },
            Close = new decimal[] { 105m, 115m, 125m },
            Volume = new decimal[] { 1000m, 1100m, 1200m },
            OI = new decimal[] { 0m, 0m, 0m },
            DateTime = new DateTime[]
            {
                Utc(2024, 1, 2, 3, 45),
                Utc(2024, 1, 3, 3, 45),
                Utc(2024, 1, 4, 3, 45)
            }
        };
    }

    private static DateTime Utc(int year, int month, int day, int hour, int minute)
    {
        return new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Utc);
    }
}
