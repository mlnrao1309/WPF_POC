using System;

namespace AlgoTrading.Models
{
    public readonly struct InstrumentCandleData
    {
        private readonly int storedInstrumentToken;

        public int InstrumentToken
        {
            get
            {
                return this.storedInstrumentToken;
            }
        }
        private readonly string storedTimeframeMinutes;

        public string TimeframeMinutes
        {
            get
            {
                return this.storedTimeframeMinutes;
            }
        }
        private readonly Candle[] storedCandles;

        public Candle[] Candles
        {
            get
            {
                return this.storedCandles;
            }
        }

        private readonly PivotPoints[]? storedPivotPoints;

        public PivotPoints[]? PivotPoints
        {
            get
            {
                return this.storedPivotPoints;
            }
        }
        private readonly Dictionary<string, double[]>? storedIndicatorData = new Dictionary<string, double[]>();

        public Dictionary<string, double[]>? IndicatorData
        {
            get
            {
                return this.storedIndicatorData;
            }
        }


        public InstrumentCandleData(int instrumentToken, string timeframeMinutes, Candle[] candles)
        {
            this.storedInstrumentToken = instrumentToken;
            this.storedTimeframeMinutes = timeframeMinutes;
            if (candles == null)
            {
                throw new ArgumentNullException(nameof(candles));
            }
            this.storedCandles = (Candle[])candles.Clone();
            this.storedPivotPoints = null;
        }

        public void Add(string indicatorName, double[] indicatorValues)
        {
            if (IndicatorData == null)
            {
                throw new InvalidOperationException("IndicatorData dictionary is not initialized.");
            }
            IndicatorData[indicatorName] = indicatorValues;
        }

        public void AddPivotPoints(Candle[] candles)
        {
            throw new NotSupportedException("Global pivots must be supplied by the pivot service; candle data does not calculate them.");
        }
    }
}
