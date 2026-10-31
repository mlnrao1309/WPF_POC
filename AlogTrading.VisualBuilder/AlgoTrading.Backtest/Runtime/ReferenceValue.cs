using AlgoTrading.Models;

namespace AlgoTrading.Backtest.Runtime;

public sealed record ReferenceValue(Candle Candle, decimal? CapturedValue);