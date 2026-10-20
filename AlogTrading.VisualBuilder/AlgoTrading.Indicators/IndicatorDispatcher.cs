using System;
using AlgoTrading.Models;

namespace AlgoTrading.Indicators;

public static class IndicatorDispatcher
{
    /// <summary>
    /// Parameter order matches IndicatorDescriptor.Parameters for each kind.
    /// Caller pre-clears all output spans; only outputs [0..actualOutputCount-1]
    /// are written.
    /// </summary>
    public static void EvaluateIndicator(
        IndicatorKind kind,
        ReadOnlySpan<decimal> source,
        ReadOnlySpan<decimal> high,
        ReadOnlySpan<decimal> low,
        ReadOnlySpan<decimal> close,
        ReadOnlySpan<decimal> volume,
        ReadOnlySpan<DateTime> dateTime,
        ReadOnlySpan<decimal> parameters,
        Span<decimal?> output1,
        Span<decimal?> output2,
        Span<decimal?> output3,
        Span<decimal?> output4,
        Span<decimal?> output5,
        Span<decimal?> output6,
        Span<decimal?> output7,
        Span<decimal?> output8,
        out int actualOutputCount)
    {
        switch (kind)
        {
            case IndicatorKind.Sma:
                Sma.Calculate(source, (int)parameters[0], output1);
                actualOutputCount = 1; return;

            case IndicatorKind.Ema:
                Ema.Calculate(source, (int)parameters[0], output1);
                actualOutputCount = 1; return;

            case IndicatorKind.Dema:
                Dema.Calculate(source, (int)parameters[0], output1);
                actualOutputCount = 1; return;

            case IndicatorKind.Tema:
                Tema.Calculate(source, (int)parameters[0], output1);
                actualOutputCount = 1; return;

            case IndicatorKind.Wma:
                Wma.Calculate(source, (int)parameters[0], output1);
                actualOutputCount = 1; return;

            case IndicatorKind.Hma:
                Hma.Calculate(source, (int)parameters[0], output1);
                actualOutputCount = 1; return;

            case IndicatorKind.Rsi:
                Rsi.Calculate(source, (int)parameters[0], output1);
                actualOutputCount = 1; return;

            case IndicatorKind.Macd:
                Macd.Calculate(source,
                    (int)parameters[0], (int)parameters[1], (int)parameters[2],
                    output1, output2, output3);
                actualOutputCount = 3; return;

            case IndicatorKind.BollingerBands:
                BollingerBands.Calculate(source,
                    (int)parameters[0], parameters[1],
                    output1, output2, output3);
                actualOutputCount = 3; return;

            case IndicatorKind.Atr:
                Atr.Calculate(high, low, close, (int)parameters[0], output1);
                actualOutputCount = 1; return;

            case IndicatorKind.Adx:
                Adx.Calculate(high, low, close, (int)parameters[0], output1, output2, output3);
                actualOutputCount = 3; return;

            case IndicatorKind.Supertrend:
                Supertrend.Calculate(high, low, close,
                    (int)parameters[0], parameters[1],
                    output1, output2);
                actualOutputCount = 2; return;

            case IndicatorKind.Vwap:
                Vwap.Calculate(high, low, close, volume, dateTime, output1);
                actualOutputCount = 1; return;

            case IndicatorKind.Obv:
                Obv.Calculate(close, volume, output1);
                actualOutputCount = 1; return;

            case IndicatorKind.Adl:
                Adl.Calculate(high, low, close, volume, output1);
                actualOutputCount = 1; return;

            case IndicatorKind.Stochastic:
                Stochastic.Calculate(high, low, close,
                    (int)parameters[0], (int)parameters[1],
                    output1, output2);
                actualOutputCount = 2; return;

            case IndicatorKind.Cci:
                Cci.Calculate(high, low, close, (int)parameters[0], output1);
                actualOutputCount = 1; return;

            case IndicatorKind.PivotPointsClassic:
                PivotPointsClassic.Calculate(high, low, close, dateTime,
                    output1, output2, output3, output4, output5, output6, output7);
                actualOutputCount = 7; return;

            case IndicatorKind.PivotPointsCamarilla:
                PivotPointsCamarilla.Calculate(high, low, close, dateTime,
                    output1, output2, output3, output4, output5, output6, output7, output8);
                actualOutputCount = 8; return;

            case IndicatorKind.VolumeSma:
                VolumeSma.Calculate(volume, (int)parameters[0], output1);
                actualOutputCount = 1; return;

            case IndicatorKind.Roc:
                Roc.Calculate(source, (int)parameters[0], output1);
                actualOutputCount = 1; return;

            case IndicatorKind.Momentum:
                Momentum.Calculate(source, (int)parameters[0], output1);
                actualOutputCount = 1; return;

            case IndicatorKind.Trix:
                Trix.Calculate(source, (int)parameters[0], (int)parameters[1], output1);
                actualOutputCount = 1; return;

            case IndicatorKind.KeltnerChannel:
                KeltnerChannel.Calculate(high, low, close,
                    (int)parameters[0], (int)parameters[1], parameters[2],
                    output1, output2, output3);
                actualOutputCount = 3; return;

            case IndicatorKind.DonchianChannel:
                DonchianChannel.Calculate(high, low, (int)parameters[0],
                    output1, output2, output3);
                actualOutputCount = 3; return;

            case IndicatorKind.Mfi:
                Mfi.Calculate(high, low, close, volume, (int)parameters[0], output1);
                actualOutputCount = 1; return;

            case IndicatorKind.HighLow52W:
                HighLow52W.Calculate(high, low, (int)parameters[0], output1, output2);
                actualOutputCount = 2; return;

            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind,
                    "Unregistered indicator kind.");
        }
    }
}
