using Stage1Scanner.Core.Market;
using AlgoTrading.DataAccess.Runtime;
using AlgoTrading.DataAccess.Etl;

namespace AlgoTrading.DataAccess.Repositories
{
    public interface IInstrumentRegistry
    {
        void Initialize(RuntimeSnapshot snapshot);
        Instruments? GetByToken(long token);
        IReadOnlyCollection<Instruments> GetAll();
        
        // Returns a dictionary of outputs for the indicator
        // e.g., Key "Value" -> List<decimal?>, Key "Direction" -> List<decimal?>
        Dictionary<string, List<decimal?>>? GetIndicatorData(long token, string timeframe, string indicatorName, int period, string inputField = "Close", decimal factor = 3m);

        void UpdateLiveIndicatorSeries(long token, string timeframe, string indicatorName, int period, Dictionary<string, List<decimal?>> newOutputs, string inputField = "Close", decimal factor = 3m);
    }

    public class InstrumentRegistry : IInstrumentRegistry
    {
        private readonly object _gate = new();
        private Dictionary<long, Instruments> _instruments = new();
        private Dictionary<string, Dictionary<string, List<decimal?>>> _indicatorData = new();
        private readonly Dictionary<string, Candles> _indicatorCandles = new();

        public void Initialize(RuntimeSnapshot snapshot)
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            var indicators = new Dictionary<string, Dictionary<string, List<decimal?>>>();
            foreach (var kvp in snapshot.IndicatorSeries)
            {
                int separator = kvp.Key.LastIndexOf('_');
                if (separator < 0) throw new InvalidDataException("Invalid snapshot indicator key: " + kvp.Key);
                string key = kvp.Key[..separator];
                if (!indicators.TryGetValue(key, out var outputs))
                    indicators[key] = outputs = new Dictionary<string, List<decimal?>>();
                outputs[kvp.Key[(separator + 1)..]] = kvp.Value.ToList();
            }
            lock (_gate)
            {
                // Replace both dictionaries; a reload must not retain obsolete indicator entries.
                _instruments = new Dictionary<long, Instruments>(snapshot.Instruments);
                _indicatorData = indicators;
                _indicatorCandles.Clear();
            }
        }

        public Instruments? GetByToken(long token)
        {
            lock (_gate) return _instruments.GetValueOrDefault(token);
        }

        public IReadOnlyCollection<Instruments> GetAll()
        {
            lock (_gate) return _instruments.Values.ToArray();
        }

        public Dictionary<string, List<decimal?>>? GetIndicatorData(long token, string timeframe, string indicatorName,
            int period, string inputField = "Close", decimal factor = 3m)
        {
            var tf = WarmupEtlService.ParseTimeframe(timeframe);
            string databaseTimeframe = tf.Unit == TimeframeUnit.Minute
                ? tf.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) : tf.ToString();
            var requirement = new RequiredIndicator { Timeframe = databaseTimeframe,
                IndicatorName = indicatorName.Trim().ToUpperInvariant(), InputField = Enum.Parse<SourceField>(inputField, ignoreCase: true).ToString(),
                Period = period, Factor = factor };
            string key = $"{token}_{databaseTimeframe}_{requirement.UniqueKey}";
            lock (_gate)
            {
                if (!_instruments.TryGetValue(token, out var instrument) || !_indicatorData.ContainsKey(key)) return null;
                lock (instrument)
                {
                    var candles = instrument.GetCandles(tf);
                    // An indicator cache entry belongs to one immutable candle collection.
                    if (!_indicatorCandles.TryGetValue(key, out var source) || !ReferenceEquals(source, candles))
                    {
                        _indicatorData[key] = WarmupEtlService.ComputeIndicator(instrument, tf, requirement);
                        _indicatorCandles[key] = candles;
                    }
                    return _indicatorData[key].ToDictionary(x => x.Key, x => x.Value.ToList());
                }
            }
        }

        public void UpdateLiveIndicatorSeries(long token, string timeframe, string indicatorName, int period,
            Dictionary<string, List<decimal?>> newOutputs, string inputField = "Close", decimal factor = 3m)
        {
            var tf = WarmupEtlService.ParseTimeframe(timeframe);
            string databaseTimeframe = tf.Unit == TimeframeUnit.Minute
                ? tf.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) : tf.ToString();
            var requirement = new RequiredIndicator { IndicatorName = indicatorName.Trim().ToUpperInvariant(),
                InputField = Enum.Parse<SourceField>(inputField, ignoreCase: true).ToString(), Period = period, Factor = factor };
            string key = $"{token}_{databaseTimeframe}_{requirement.UniqueKey}";
            lock (_gate)
            {
                if (!_instruments.TryGetValue(token, out var instrument))
                    throw new KeyNotFoundException($"Instrument {token} is not registered.");
                lock (instrument)
                {
                    var candles = instrument.GetCandles(tf);
                    if (newOutputs.Values.Any(values => values.Count != candles.Count))
                        throw new ArgumentException("Indicator outputs must align with the current candles.", nameof(newOutputs));
                    _indicatorData[key] = newOutputs.ToDictionary(x => x.Key, x => x.Value.ToList());
                    _indicatorCandles[key] = candles;
                }
            }
        }
    }
}
