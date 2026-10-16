using System;
using AlgoTrading.Models.Rules;

namespace AlgoTrading.Models
{
    internal static class CandleValidation
    {
        internal static void Validate(CompletedCandle candle)
        {
            ArgumentNullException.ThrowIfNull(candle);
            Candle value = candle.Candle;
            if (value.High < value.Low || value.Open < value.Low || value.Open > value.High
                || value.Close < value.Low || value.Close > value.High || value.Volume < 0)
            {
                throw new ArgumentException("Source OHLC or volume is invalid.");
            }
            if (candle.ClosedAt <= value.OpenedAt)
            {
                throw new ArgumentException("Candle completion must follow its start.");
            }
        }
    }
}
