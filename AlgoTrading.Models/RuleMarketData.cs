using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace AlgoTrading.Models.Rules
{
    public sealed class RuleMarketData
    {
        private readonly Dictionary<string, CompletedCandle[]> series;
        private readonly int instrumentToken;
        private readonly string fingerprint;

        public int InstrumentToken
        {
            get
            {
                return this.instrumentToken;
            }
        }

        public string Fingerprint
        {
            get
            {
                return this.fingerprint;
            }
        }

        public RuleMarketData(int instrumentToken, IReadOnlyDictionary<string, IReadOnlyList<CompletedCandle>> completedSeries)
        {
            ArgumentNullException.ThrowIfNull(completedSeries);
            this.instrumentToken = instrumentToken;
            this.series = new Dictionary<string, CompletedCandle[]>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, IReadOnlyList<CompletedCandle>> entry in completedSeries)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(entry.Key);
                ArgumentNullException.ThrowIfNull(entry.Value);
                CompletedCandle[] candles = new CompletedCandle[entry.Value.Count];
                for (int index = 0; index < candles.Length; index++)
                {
                    CompletedCandle current = entry.Value[index];
                    if (current == null || current.Candle.InstrumentToken != instrumentToken)
                    {
                        throw new ArgumentException("Candles must be non-null and belong to the requested instrument.");
                    }
                    if (index > 0 && candles[index - 1].ClosedAt >= current.ClosedAt)
                    {
                        throw new ArgumentException("Completed candles must have unique, increasing close times.");
                    }
                    candles[index] = current;
                }
                this.series.Add(entry.Key, candles);
            }
            this.fingerprint = CalculateFingerprint();
        }

        internal CompletedCandle[] GetSeries(string timeframe)
        {
            CompletedCandle[]? candles;
            if (this.series.TryGetValue(timeframe, out candles))
            {
                return candles;
            }
            return Array.Empty<CompletedCandle>();
        }

        internal bool Contains(string timeframe)
        {
            return this.series.ContainsKey(timeframe);
        }

        internal static int FindCompletedIndex(CompletedCandle[] candles, DateTimeOffset timestamp)
        {
            int lower = 0;
            int upper = candles.Length - 1;
            while (lower <= upper)
            {
                int middle = lower + (upper - lower) / 2;
                if (candles[middle].ClosedAt <= timestamp)
                {
                    lower = middle + 1;
                }
                else
                {
                    upper = middle - 1;
                }
            }
            return upper;
        }

        private string CalculateFingerprint()
        {
            List<string> keys = new List<string>(this.series.Keys);
            keys.Sort(StringComparer.Ordinal);
            using (MemoryStream buffer = new MemoryStream())
            {
                using (BinaryWriter writer = new BinaryWriter(buffer, Encoding.UTF8, true))
                {
                    writer.Write(this.instrumentToken);
                    writer.Write(keys.Count);
                    foreach (string key in keys)
                    {
                        writer.Write(key);
                        writer.Write(this.series[key].Length);
                        foreach (CompletedCandle item in this.series[key])
                        {
                            Candle candle = item.Candle;
                            writer.Write(candle.TimeframeMinutes);
                            writer.Write(candle.OpenedAt.UtcTicks);
                            writer.Write(item.ClosedAt.UtcTicks);
                            writer.Write(candle.Open);
                            writer.Write(candle.High);
                            writer.Write(candle.Low);
                            writer.Write(candle.Close);
                            writer.Write(candle.Volume);
                        }
                    }
                }
                return Convert.ToHexString(SHA256.HashData(buffer.ToArray()));
            }
        }
    }
}
