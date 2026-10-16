using Stage1Scanner.Core.Indicators;
using Stage1Scanner.Core.Market;
using Stage1Scanner.Core.Rules;
using Stage1Scanner.Core.Series;
using CoreCandle = Stage1Scanner.Core.Market.Candle;

namespace AlgoTrading.Models.Rules;

internal static class CoreIndicatorSeriesCalculator
{
    internal static bool Supports(IndicatorFunction function) => function >= IndicatorFunction.DoubleExponentialMovingAverage;

    internal static double[] Calculate(IndicatorValue indicator, CompletedCandle[] source)
    {
        Timeframe timeframe = ParseTimeframe(indicator.Timeframe);
        CoreCandle[] values = new CoreCandle[source.Length];
        for (int i = 0; i < source.Length; i++)
        {
            Candle candle = source[i].Candle;
            values[i] = new CoreCandle(candle.OpenedAt, source[i].ClosedAt, candle.Open, candle.High, candle.Low,
                candle.Close, candle.Volume);
        }

        Candles candles = new Candles(timeframe, values);
        SourceField field = SourceField.Close;
        if (indicator.Source is CandleValue candleValue)
        {
            field = candleValue.Field switch
            {
                CandleField.Open => SourceField.Open,
                CandleField.High => SourceField.High,
                CandleField.Low => SourceField.Low,
                CandleField.Close => SourceField.Close,
                CandleField.Volume => SourceField.Volume,
                _ => SourceField.Close
            };
        }

        NumericSeries series = Resolve(candles, indicator, field);
        double[] result = new double[series.Count];
        for (int i = 0; i < result.Length; i++) result[i] = series[i].HasValue ? (double)series[i]!.Value : double.NaN;
        return result;
    }

    private static NumericSeries Resolve(Candles candles, IndicatorValue indicator, SourceField source)
    {
        int p1 = indicator.Length;
        int p2 = indicator.Period2;
        int p3 = indicator.Period3;
        decimal multiplier = (decimal)indicator.Multiplier;
        return indicator.Function switch
        {
            IndicatorFunction.DoubleExponentialMovingAverage => candles.DEMA(p1, source),
            IndicatorFunction.TripleExponentialMovingAverage => candles.TEMA(p1, source),
            IndicatorFunction.WeightedMovingAverage => candles.WMA(p1, source),
            IndicatorFunction.HullMovingAverage => candles.HMA(p1, source),
            IndicatorFunction.MovingAverageConvergenceDivergence => SelectMacd(candles.MACD(p1, p2 > 0 ? p2 : 26, p3 > 0 ? p3 : 9, source), indicator.Component),
            IndicatorFunction.AverageTrueRange => candles.ATR(p1),
            IndicatorFunction.AverageDirectionalIndex => SelectAdx(candles.ADX(p1), indicator.Component),
            IndicatorFunction.VolumeWeightedAveragePrice => candles.VWAP(),
            IndicatorFunction.OnBalanceVolume => candles.OBV(),
            IndicatorFunction.Stochastic => SelectStochastic(candles.Stochastic(p1, p2 > 0 ? p2 : 3), indicator.Component),
            IndicatorFunction.CommodityChannelIndex => candles.CCI(p1),
            IndicatorFunction.VolumeSimpleMovingAverage => candles.VolumeSMA(p1),
            IndicatorFunction.High52Week => candles.High52W(p1),
            IndicatorFunction.Low52Week => candles.Low52W(p1),
            IndicatorFunction.RateOfChange => candles.ROC(p1, source),
            IndicatorFunction.Momentum => candles.Momentum(p1, source),
            IndicatorFunction.TripleExponentialAverage => candles.TRIX(p1, source),
            IndicatorFunction.KeltnerChannel => SelectKeltner(candles.Keltner(p1, p2 > 0 ? p2 : 10, multiplier), indicator.Component),
            IndicatorFunction.DonchianChannel => SelectDonchian(candles.Donchian(p1), indicator.Component),
            _ => throw new NotSupportedException($"Core indicator {indicator.Function} is not supported.")
        };
    }

    private static NumericSeries SelectMacd(MacdSeries value, IndicatorComponent component) => component switch
    {
        IndicatorComponent.Signal => value.Signal,
        IndicatorComponent.Histogram => value.Histogram,
        _ => value.Macd
    };

    private static NumericSeries SelectAdx(AdxSeries value, IndicatorComponent component) => component switch
    {
        IndicatorComponent.PlusDi => value.PlusDi,
        IndicatorComponent.MinusDi => value.MinusDi,
        _ => value.Adx
    };

    private static NumericSeries SelectStochastic(StochasticSeries value, IndicatorComponent component) =>
        component == IndicatorComponent.D ? value.D : value.K;

    private static NumericSeries SelectKeltner(KeltnerSeries value, IndicatorComponent component) => component switch
    {
        IndicatorComponent.Upper => value.Upper,
        IndicatorComponent.Lower => value.Lower,
        _ => value.Basis
    };

    private static NumericSeries SelectDonchian(DonchianSeries value, IndicatorComponent component) => component switch
    {
        IndicatorComponent.Upper => value.Upper,
        IndicatorComponent.Lower => value.Lower,
        _ => value.Middle
    };

    private static Timeframe ParseTimeframe(string value) => value switch
    {
        "minute" => Timeframe.Minutes(1),
        "3minute" => Timeframe.Minutes(3),
        "5minute" => Timeframe.Minutes(5),
        "10minute" => Timeframe.Minutes(10),
        "15minute" => Timeframe.Minutes(15),
        "30minute" => Timeframe.Minutes(30),
        "hour" => Timeframe.Hours(1),
        "2hour" => Timeframe.Hours(2),
        "day" => Timeframe.Day,
        "week" => Timeframe.Week,
        _ => throw new ArgumentException($"Unsupported indicator timeframe '{value}'.", nameof(value))
    };
}
