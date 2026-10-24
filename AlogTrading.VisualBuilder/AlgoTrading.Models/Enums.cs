namespace AlgoTrading.Models;

public enum Direction { Long, Short }

public enum Field { Open, High, Low, Close, Volume, OI }

public enum GroupLogic { All, Any }

public enum ExecutionMode { Delivery, Intraday }

public enum RiskLossBasis { RealizedOnly, IncludeUnrealized }

public enum RiskPeriod { Daily, Monthly }

public enum RiskAssessmentStatus { NotAssessed, Approved, Rejected }

public enum RiskAssessmentReason { None, InsufficientData, NoClosedTrades, UnsupportedRunMode, LossLimitBreached }

public enum StopLossMode { Percentage, Points, Rule }

public enum TargetMode { Percentage, Points, Rule }

public enum TimeframeUnit { m, H, D, W, M }

public enum InstrumentType { Equity, Index, Future, Option, CM }

public enum CalculationOp { Add, Subtract, Multiply, Divide, Modulo, Min, Max }

public enum Operator
{
    GreaterThan,
    LessThan,
    GreaterOrEqual,
    LessOrEqual,
    Equal,
    NotEqual,
    CrossedAbove,
    CrossedBelow,
    OpenedAbove,
    OpenedBelow,
    ClosedAbove,
    ClosedBelow
}

public enum ExitReason
{
    FullExit,
    Protection,
    ExitCondition,
    StopLoss,
    Target,
    EndOfData
}

// Section 2.8.10 â€” enumerated in Section 4 (27 indicators).
public enum IndicatorKind
{
    Sma, Ema, Dema, Tema, Wma, Hma,
    Rsi, Macd, BollingerBands,
    Atr, Adx, Supertrend, Vwap, Obv, Adl,
    Stochastic, Cci,
    PivotPointsClassic, PivotPointsCamarilla,
    VolumeSma, Roc, Momentum, Trix,
    KeltnerChannel, DonchianChannel, Mfi, HighLow52W
}
