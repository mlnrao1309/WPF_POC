namespace Stage1Scanner.Core.Market;

/// <summary>
/// One resolved market instrument together with the candle series loaded for it.
/// Different timeframes are stored separately; each Candles instance is homogeneous.
/// </summary>
public sealed class Instruments
{
    private readonly Dictionary<Timeframe, Candles> _candles = new();

    public Instruments(long instrumentToken, string symbol)
    {
        InstrumentToken = instrumentToken;
        Symbol = string.IsNullOrWhiteSpace(symbol)
            ? throw new ArgumentException("Symbol is required.", nameof(symbol))
            : symbol;
    }

    public long InstrumentToken { get; }
    public string Symbol { get; }

    public CriticalStructureStore Structures { get; } = new();

    public ReferenceStore References { get; } = new();

    public IEnumerable<Timeframe> AvailableTimeframes
    {
        get { lock (this) return _candles.Keys.ToArray(); }
    }

    public void SetCandles(Timeframe timeframe, IEnumerable<Candle> candles)
    {
        lock (this) _candles[timeframe] = new Candles(timeframe, candles);
    }

    public void SetCandles(Candles candles)
    {
        ArgumentNullException.ThrowIfNull(candles);
        lock (this) _candles[candles.Timeframe] = candles;
    }

    public Candles GetCandles(Timeframe timeframe)
    {
        lock (this)
        {
            if (!_candles.TryGetValue(timeframe, out var candles))
            {
                throw new InvalidOperationException(
                    $"No candles loaded for {Symbol} at timeframe {timeframe}.");
            }

            return candles;
        }
    }

    public bool TryGetCandles(Timeframe timeframe, out Candles candles)
    {
        lock (this) return _candles.TryGetValue(timeframe, out candles!);
    }
}
