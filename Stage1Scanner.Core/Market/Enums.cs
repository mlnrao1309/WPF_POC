namespace Stage1Scanner.Core.Market;

public enum SourceField
{
    Open,
    High,
    Low,
    Close,
    Volume,
    OpenInterest
}

public enum TrendDirection
{
    Bullish,
    Bearish,
    RangeBound,
    Transition
}

public enum IndicatorType
{
    SMA,
    EMA,
    DEMA,
    TEMA,
    WMA,
    HMA,
    RSI,
    MACD,
    BollingerBands,
    ATR,
    ADX,
    Supertrend,
    VWAP,
    OBV,
    Stochastic,
    CCI,
    VolumeSMA,
    High52W,
    Low52W,
    ROC,
    Momentum,
    TRIX,
    Keltner,
    Donchian
}

public enum MovingAverageType
{
    SMA,
    EMA,
    WMA
}
