namespace AlgoTrading.RuleEditor.Services;

public sealed class IndicatorParamInfo
{
    public string Name { get; init; } = "";
    public decimal Default { get; init; }
    public decimal Minimum { get; init; }
    public decimal Maximum { get; init; }
    public bool IntegerOnly { get; init; }
}

public sealed class IndicatorDescriptorDto
{
    public string Kind { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string SourceKind { get; init; } = "Single"; // Single | None | Optional

    /// <summary>
    /// True for pivot-point indicators. These:
    ///   * accept only Daily / Weekly / Monthly source timeframes;
    ///   * have no price source (High/Low/Close come from the pivot candle itself);
    ///   * serialize to the engine's PivotOperand (Classic) or to
    ///     IndicatorType.Camarilla with an R1-R4 / S1-S4 component selector.
    /// </summary>
    public bool IsPivot { get; init; }

    public IReadOnlyList<IndicatorParamInfo> Parameters { get; init; } = Array.Empty<IndicatorParamInfo>();
    public IReadOnlyList<string> Outputs { get; init; } = new[] { "Value" };
}

/// <summary>
/// Static catalog of indicators the editor exposes. Mirrors Section 4.5 so the
/// editor does not need to reference the Indicators project. If a strategy uses
/// an indicator not listed here, the validator (RULE_014) reports it and the
/// user can still author the JSON by hand.
/// </summary>
public static class IndicatorCatalogService
{
    public static IReadOnlyList<IndicatorDescriptorDto> All { get; } = Build();

    public static IndicatorDescriptorDto? Find(string kind)
    {
        foreach (var d in All)
            if (string.Equals(d.Kind, kind, StringComparison.Ordinal)) return d;
        return null;
    }

    private static IReadOnlyList<IndicatorDescriptorDto> Build()
    {
        var list = new List<IndicatorDescriptorDto>(27);
        void Add(string k, string d, string src, string[] outs, params IndicatorParamInfo[] ps)
        {
            list.Add(new IndicatorDescriptorDto
            {
                Kind = k, DisplayName = d, SourceKind = src,
                IsPivot = k is "PivotPointsClassic" or "PivotPointsCamarilla",
                Outputs = outs, Parameters = ps
            });
        }
        IndicatorParamInfo P(string n, decimal d, decimal lo, decimal hi, bool intOnly = true)
            => new() { Name = n, Default = d, Minimum = lo, Maximum = hi, IntegerOnly = intOnly };

        Add("Sma", "SMA", "Single", new[] { "Value" }, P("period", 20, 1, 1000));
        Add("Ema", "EMA", "Single", new[] { "Value" }, P("period", 20, 1, 1000));
        Add("Dema", "DEMA", "Single", new[] { "Value" }, P("period", 20, 1, 1000));
        Add("Tema", "TEMA", "Single", new[] { "Value" }, P("period", 20, 1, 1000));
        Add("Wma", "WMA", "Single", new[] { "Value" }, P("period", 20, 1, 1000));
        Add("Hma", "HMA", "Single", new[] { "Value" }, P("period", 20, 1, 1000));
        Add("Rsi", "RSI", "Single", new[] { "Value" }, P("period", 14, 1, 1000));
        Add("Macd", "MACD", "Single", new[] { "Macd", "Signal", "Histogram" },
            P("fast", 12, 1, 1000), P("slow", 26, 1, 1000), P("signal", 9, 1, 1000));
        Add("BollingerBands", "Bollinger Bands", "Single", new[] { "Upper", "Middle", "Lower" },
            P("period", 20, 1, 1000), P("stdDev", 2.0m, 0.01m, 100m, intOnly: false));
        Add("Atr", "ATR", "None", new[] { "Value" }, P("period", 14, 1, 1000));
        Add("Adx", "ADX", "None", new[] { "Adx", "PlusDi", "MinusDi" }, P("period", 14, 1, 1000));
        
        // FIXED: "Line" -> "Value", and added transition states from engine IndicatorComponent
        Add("Supertrend", "Supertrend", "None", new[] { "Value", "Direction", "TurnedBullish", "TurnedBearish" },
            P("period", 10, 1, 1000), P("multiplier", 3.0m, 0.01m, 100m, intOnly: false));
            
        Add("Vwap", "VWAP", "None", new[] { "Value" });
        Add("Obv", "OBV", "None", new[] { "Value" });
        Add("Adl", "Accumulation/Distribution", "None", new[] { "Value" });
        Add("Stochastic", "Stochastic", "None", new[] { "K", "D" },
            P("kPeriod", 14, 1, 1000), P("dPeriod", 3, 1, 1000));
        Add("Cci", "CCI", "None", new[] { "Value" }, P("period", 20, 1, 1000));

        // Classic pivots — level list mirrors engine PivotLevel (P, R1-R5, S1-S5).
        // "PP" is the editor label for PivotLevel.P and is normalized to "P" on serialize.
        Add("PivotPointsClassic", "Pivot Points (Classic)", "None",
            new[] { "PP", "R1", "R2", "R3", "R4", "R5", "S1", "S2", "S3", "S4", "S5" });

        // Camarilla — component list mirrors engine IndicatorComponent (R1-R4, S1-S4).
        Add("PivotPointsCamarilla", "Pivot Points (Camarilla)", "None",
            new[] { "R4", "R3", "R2", "R1", "S1", "S2", "S3", "S4" });

        Add("VolumeSma", "Volume SMA", "None", new[] { "Value" }, P("period", 20, 1, 1000));
        Add("Roc", "ROC", "Single", new[] { "Value" }, P("period", 12, 1, 1000));
        Add("Momentum", "Momentum", "Single", new[] { "Value" }, P("period", 10, 1, 1000));
        Add("Trix", "TRIX", "Single", new[] { "Value" },
            P("period", 15, 1, 1000), P("signal", 9, 1, 1000));
        Add("KeltnerChannel", "Keltner Channels", "None", new[] { "Upper", "Middle", "Lower" },
            P("period", 20, 1, 1000), P("atrPeriod", 10, 1, 1000),
            P("multiplier", 2.0m, 0.01m, 100m, intOnly: false));
        Add("DonchianChannel", "Donchian Channels", "None", new[] { "Upper", "Middle", "Lower" },
            P("period", 20, 1, 1000));
        Add("Mfi", "Money Flow Index", "None", new[] { "Value" }, P("period", 14, 1, 1000));
        Add("HighLow52W", "52-Week High/Low", "None", new[] { "High52", "Low52" },
            P("period", 250, 1, 5000));

        Add("Count", "Count", "None", new[] { "Value" });
        return list;
    }
}