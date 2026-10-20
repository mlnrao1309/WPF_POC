using System;
using System.Collections.Generic;
using AlgoTrading.Models;

namespace AlgoTrading.Indicators;

public static class IndicatorRegistry
{
    private static readonly Dictionary<IndicatorKind, IndicatorDescriptor> _map;
    private static readonly IndicatorDescriptor[] _all;

    static IndicatorRegistry()
    {
        var list = new List<IndicatorDescriptor>(27);

        IndicatorParameter P(string n, decimal d, decimal min, decimal max, bool intOnly = true)
            => new() { Name = n, Default = d, Minimum = min, Maximum = max, IntegerOnly = intOnly };

        void Add(IndicatorKind k, string disp, SourceKind src, IndicatorParameter[] ps,
                 string[] outs, int warm)
            => list.Add(new IndicatorDescriptor
            {
                Kind = k,
                DisplayName = disp,
                Source = src,
                Parameters = ps,
                Outputs = outs,
                WarmupBarsAtDefault = warm
            });

        Add(IndicatorKind.Sma, "SMA", SourceKind.Single,
            [P("period", 20, 1, 500)], ["Value"], 19);

        Add(IndicatorKind.Ema, "EMA", SourceKind.Single,
            [P("period", 20, 1, 500)], ["Value"], 19);

        Add(IndicatorKind.Dema, "DEMA", SourceKind.Single,
            [P("period", 20, 1, 500)], ["Value"], 38);

        Add(IndicatorKind.Tema, "TEMA", SourceKind.Single,
            [P("period", 20, 1, 500)], ["Value"], 57);

        Add(IndicatorKind.Wma, "WMA", SourceKind.Single,
            [P("period", 20, 1, 500)], ["Value"], 19);

        Add(IndicatorKind.Hma, "HMA", SourceKind.Single,
            [P("period", 20, 2, 500)], ["Value"], 19);

        Add(IndicatorKind.Rsi, "RSI", SourceKind.Single,
            [P("period", 14, 2, 500)], ["Value"], 14);

        Add(IndicatorKind.Macd, "MACD", SourceKind.Single,
            [P("fast", 12, 1, 500), P("slow", 26, 1, 500), P("signal", 9, 1, 500)],
            ["Macd", "Signal", "Histogram"], 35);

        Add(IndicatorKind.BollingerBands, "Bollinger Bands", SourceKind.Single,
            [P("period", 20, 2, 500), P("stdDev", 2.0m, 0.1m, 10m, false)],
            ["Upper", "Middle", "Lower"], 19);

        Add(IndicatorKind.Atr, "ATR", SourceKind.None,
            [P("period", 14, 1, 500)], ["Value"], 14);

        Add(IndicatorKind.Adx, "ADX", SourceKind.None,
            [P("period", 14, 2, 500)], ["Adx", "PlusDi", "MinusDi"], 28);

        Add(IndicatorKind.Supertrend, "Supertrend", SourceKind.None,
            [P("period", 10, 1, 500), P("multiplier", 3.0m, 0.1m, 20m, false)],
            ["Line", "Direction"], 10);

        Add(IndicatorKind.Vwap, "VWAP", SourceKind.None, [], ["Value"], 1);

        Add(IndicatorKind.Obv, "OBV", SourceKind.None, [], ["Value"], 1);

        Add(IndicatorKind.Adl, "Accumulation/Distribution", SourceKind.None, [], ["Value"], 1);

        Add(IndicatorKind.Stochastic, "Stochastic", SourceKind.None,
            [P("kPeriod", 14, 1, 500), P("dPeriod", 3, 1, 500)], ["K", "D"], 17);

        Add(IndicatorKind.Cci, "CCI", SourceKind.None,
            [P("period", 20, 1, 500)], ["Value"], 20);

        Add(IndicatorKind.PivotPointsClassic, "Pivot Points (Classic)", SourceKind.None,
            [], ["PP", "R1", "R2", "R3", "S1", "S2", "S3"], 1);

        Add(IndicatorKind.PivotPointsCamarilla, "Pivot Points (Camarilla)", SourceKind.None,
            [], ["R4", "R3", "R2", "R1", "S1", "S2", "S3", "S4"], 1);

        Add(IndicatorKind.VolumeSma, "Volume SMA", SourceKind.None,
            [P("period", 20, 1, 500)], ["Value"], 19);

        Add(IndicatorKind.Roc, "ROC", SourceKind.Single,
            [P("period", 12, 1, 500)], ["Value"], 12);

        Add(IndicatorKind.Momentum, "Momentum", SourceKind.Single,
            [P("period", 10, 1, 500)], ["Value"], 10);

        Add(IndicatorKind.Trix, "TRIX", SourceKind.Single,
            [P("period", 15, 1, 500), P("signal", 9, 1, 500)], ["Value"], 54);

        Add(IndicatorKind.KeltnerChannel, "Keltner Channels", SourceKind.None,
            [P("period", 20, 1, 500), P("atrPeriod", 10, 1, 500),
             P("multiplier", 2.0m, 0.1m, 20m, false)],
            ["Upper", "Middle", "Lower"], 30);

        Add(IndicatorKind.DonchianChannel, "Donchian Channels", SourceKind.None,
            [P("period", 20, 1, 500)], ["Upper", "Middle", "Lower"], 19);

        Add(IndicatorKind.Mfi, "Money Flow Index", SourceKind.None,
            [P("period", 14, 1, 500)], ["Value"], 14);

        Add(IndicatorKind.HighLow52W, "52-Week High/Low", SourceKind.None,
            [P("period", 250, 1, 2000)], ["High52", "Low52"], 249);

        _all = list.ToArray();
        _map = new Dictionary<IndicatorKind, IndicatorDescriptor>(_all.Length);
        foreach (var d in _all) _map[d.Kind] = d;

        if (_all.Length != 27)
            throw new InvalidOperationException(
                $"IndicatorRegistry must contain exactly 27 entries, found {_all.Length}.");
    }

    public static IReadOnlyCollection<IndicatorDescriptor> All => _all;

    public static bool TryGet(IndicatorKind kind, out IndicatorDescriptor descriptor)
        => _map.TryGetValue(kind, out descriptor!);

    public static IndicatorDescriptor Get(IndicatorKind kind)
    {
        if (!_map.TryGetValue(kind, out var d))
            throw new ArgumentOutOfRangeException(nameof(kind), kind,
                "Indicator kind is not registered.");
        return d;
    }
}