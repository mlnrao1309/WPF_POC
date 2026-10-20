using System;
using AlgoTrading.Data;
using AlgoTrading.Indicators;
using AlgoTrading.Models;

namespace AlgoTrading.Backtest;

internal static class IndicatorDispatch
{
    public const int MaxOutputs = 8;

    public static void Execute(
        IndicatorKind kind,
        IBarSeries series,
        ReadOnlySpan<decimal> source,
        ReadOnlySpan<decimal> parameters,
        decimal?[][] outputs,
        out int actualOutputCount)
    {
        var high   = series.High.Span;
        var low    = series.Low.Span;
        var close  = series.Close.Span;
        var volume = series.Volume.Span;
        var dt     = series.DateTime.Span;

        var o1 = outputs[0].AsSpan();
        var o2 = outputs[1].AsSpan();
        var o3 = outputs[2].AsSpan();
        var o4 = outputs[3].AsSpan();
        var o5 = outputs[4].AsSpan();
        var o6 = outputs[5].AsSpan();
        var o7 = outputs[6].AsSpan();
        var o8 = outputs[7].AsSpan();

        switch (kind)
        {
            case IndicatorKind.Sma:   Sma.Calculate(source, (int)parameters[0], o1);   actualOutputCount = 1; return;
            case IndicatorKind.Ema:   Ema.Calculate(source, (int)parameters[0], o1);   actualOutputCount = 1; return;
            case IndicatorKind.Dema:  Dema.Calculate(source, (int)parameters[0], o1);  actualOutputCount = 1; return;
            case IndicatorKind.Tema:  Tema.Calculate(source, (int)parameters[0], o1);  actualOutputCount = 1; return;
            case IndicatorKind.Wma:   Wma.Calculate(source, (int)parameters[0], o1);   actualOutputCount = 1; return;
            case IndicatorKind.Hma:   Hma.Calculate(source, (int)parameters[0], o1);   actualOutputCount = 1; return;
            case IndicatorKind.Rsi:   Rsi.Calculate(source, (int)parameters[0], o1);   actualOutputCount = 1; return;
            case IndicatorKind.Macd:  Macd.Calculate(source, (int)parameters[0], (int)parameters[1], (int)parameters[2], o1, o2, o3); actualOutputCount = 3; return;
            case IndicatorKind.BollingerBands: BollingerBands.Calculate(source, (int)parameters[0], parameters[1], o1, o2, o3); actualOutputCount = 3; return;
            case IndicatorKind.Atr:   Atr.Calculate(high, low, close, (int)parameters[0], o1); actualOutputCount = 1; return;
            case IndicatorKind.Adx:   Adx.Calculate(high, low, close, (int)parameters[0], o1, o2, o3); actualOutputCount = 3; return;
            case IndicatorKind.Supertrend: Supertrend.Calculate(high, low, close, (int)parameters[0], parameters[1], o1, o2); actualOutputCount = 2; return;
            case IndicatorKind.Vwap:  Vwap.Calculate(high, low, close, volume, dt, o1); actualOutputCount = 1; return;
            case IndicatorKind.Obv:   Obv.Calculate(close, volume, o1); actualOutputCount = 1; return;
            case IndicatorKind.Adl:   Adl.Calculate(high, low, close, volume, o1); actualOutputCount = 1; return;
            case IndicatorKind.Stochastic: Stochastic.Calculate(high, low, close, (int)parameters[0], (int)parameters[1], o1, o2); actualOutputCount = 2; return;
            case IndicatorKind.Cci:   Cci.Calculate(high, low, close, (int)parameters[0], o1); actualOutputCount = 1; return;
            case IndicatorKind.PivotPointsClassic:   PivotPointsClassic.Calculate(high, low, close, dt, o1, o2, o3, o4, o5, o6, o7); actualOutputCount = 7; return;
            case IndicatorKind.PivotPointsCamarilla: PivotPointsCamarilla.Calculate(high, low, close, dt, o1, o2, o3, o4, o5, o6, o7, o8); actualOutputCount = 8; return;
            case IndicatorKind.VolumeSma: VolumeSma.Calculate(volume, (int)parameters[0], o1); actualOutputCount = 1; return;
            case IndicatorKind.Roc:   Roc.Calculate(source, (int)parameters[0], o1); actualOutputCount = 1; return;
            case IndicatorKind.Momentum: Momentum.Calculate(source, (int)parameters[0], o1); actualOutputCount = 1; return;
            case IndicatorKind.Trix:  Trix.Calculate(source, (int)parameters[0], (int)parameters[1], o1); actualOutputCount = 1; return;
            case IndicatorKind.KeltnerChannel: KeltnerChannel.Calculate(high, low, close, (int)parameters[0], (int)parameters[1], parameters[2], o1, o2, o3); actualOutputCount = 3; return;
            case IndicatorKind.DonchianChannel: DonchianChannel.Calculate(high, low, (int)parameters[0], o1, o2, o3); actualOutputCount = 3; return;
            case IndicatorKind.Mfi:   Mfi.Calculate(high, low, close, volume, (int)parameters[0], o1); actualOutputCount = 1; return;
            case IndicatorKind.HighLow52W: HighLow52W.Calculate(high, low, (int)parameters[0], o1, o2); actualOutputCount = 2; return;
            default: throw new InvalidOperationException($"Unsupported IndicatorKind: {kind}");
        }
    }
}