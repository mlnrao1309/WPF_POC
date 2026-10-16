using AlgoTrading.Models.Rules;

internal sealed record DatabaseCandleRow(
    int InstrumentToken,
    decimal Open,
    decimal Close,
    decimal High,
    decimal Low,
    long? OpenInterest,
    long Volume,
    DateTime Timestamp,
    DateTime? LastUpdated,
    string Timeframe,
    CompletedCandle CompletedCandle);
