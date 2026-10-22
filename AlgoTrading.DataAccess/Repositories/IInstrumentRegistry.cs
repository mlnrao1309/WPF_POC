using Stage1Scanner.Core.Market;
using AlgoTrading.DataAccess.Runtime;

namespace AlgoTrading.DataAccess.Repositories
{
    public interface IInstrumentRegistry
    {
        void Initialize(RuntimeSnapshot snapshot);
        Instruments? GetByToken(long token);
        IReadOnlyCollection<Instruments> GetAll();
        
        // Returns a dictionary of outputs for the indicator
        // e.g., Key "Value" -> List<decimal?>, Key "Direction" -> List<decimal?>
        Dictionary<string, List<decimal?>>? GetIndicatorData(long token, string timeframe, string indicatorName, int period);

        void UpdateLiveIndicatorSeries(long token, string timeframe, string indicatorName, int period, Dictionary<string, List<decimal?>> newOutputs);
    }

    public class InstrumentRegistry : IInstrumentRegistry
    {
        private Dictionary<long, Instruments> _instruments = new();

        // Key: "{Token}_{Timeframe}_{IndicatorName}_{Period}" -> Value: Output Dict
        private Dictionary<string, Dictionary<string, List<decimal?>>> _indicatorData = new();

        public void Initialize(RuntimeSnapshot snapshot)
        {
            _instruments = snapshot.Instruments;

            // Reconstruct the hierarchical dictionary
            foreach (var kvp in snapshot.IndicatorSeries)
            {
                // kvp.Key is like "265_60_SUPERTREND_10_Value"
                string[] parts = kvp.Key.Split('_');
                if (parts.Length < 5) continue;

                string baseKey = $"{parts[0]}_{parts[1]}_{parts[2]}_{parts[3]}"; // "265_60_SUPERTREND_10"
                string outputName = parts[4]; // "Value" or "Direction"

                if (!_indicatorData.TryGetValue(baseKey, out var outputs))
                {
                    _indicatorData[baseKey] = outputs = new Dictionary<string, List<decimal?>>();
                }
                outputs[outputName] = kvp.Value;
            }
        }

        public Instruments? GetByToken(long token)
        {
            return _instruments.TryGetValue(token, out var inst) ? inst : null;
        }

        public IReadOnlyCollection<Instruments> GetAll()
        {
            return _instruments.Values.ToList();
        }

        public Dictionary<string, List<decimal?>>? GetIndicatorData(long token, string timeframe, string indicatorName, int period)
        {
            string key = $"{token}_{timeframe}_{indicatorName}_{period}";
            return _indicatorData.TryGetValue(key, out var outputs) ? outputs : null;
        }

        // Add this to the IInstrumentRegistry interface
        // void UpdateLiveIndicatorSeries(long token, string timeframe, string indicatorName, int period, Dictionary<string, List<decimal?>> newOutputs);

        // Add this implementation to the InstrumentRegistry class
        public void UpdateLiveIndicatorSeries(long token, string timeframe, string indicatorName, int period, Dictionary<string, List<decimal?>> newOutputs)
        {
            string baseKey = $"{token}_{timeframe}_{indicatorName}_{period}";

            if (!_indicatorData.TryGetValue(baseKey, out var outputs))
            {
                _indicatorData[baseKey] = outputs = new Dictionary<string, List<decimal?>>();
            }

            foreach (var kvp in newOutputs)
            {
                outputs[kvp.Key] = kvp.Value; // Overwrites the old list with the newly calculated list
            }
        }
    }
}