using Stage1Scanner.Core.Market;

namespace Stage1Scanner.Core.Rules;

public enum IndicatorComponent
{
    Value,
    Macd,
    Signal,
    Histogram,
    Upper,
    Lower,
    Middle,
    Basis,
    K,
    D,
    Adx,
    PlusDi,
    MinusDi,

    // Supertrend state/event operands (SCN-IND-002).
    // Direction: +1 Bullish, -1 Bearish, null during warm-up.
    // TurnedBullish / TurnedBearish: 1 on the transition bar, 0 otherwise.
    Direction,
    TurnedBullish,
    TurnedBearish,

    // RSI selector components.
    // RsiSmooth     = smoothed RSI line (requires Smoothing to be set).
    // RsiDivergence = +1 on bullish divergence, -1 on bearish, 0 otherwise.
    RsiSmooth,
    RsiDivergence
}

/// <summary>
/// Indicator definition stored inside a Rule operand.
/// Timeframe may be omitted only when Scanner timeframe inheritance is enabled.
/// </summary>
public sealed record IndicatorSpec(
    IndicatorType Kind,
    Timeframe? Timeframe = null,
    SourceField Source = SourceField.Close,
    int Period1 = 14,
    int Period2 = 0,
    int Period3 = 0,
    decimal Decimal1 = 0m,
    MovingAverageType? Smoothing = null,
    IndicatorComponent Component = IndicatorComponent.Value,
    Operand? Input = null);
