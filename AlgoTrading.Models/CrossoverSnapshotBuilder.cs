namespace AlgoTrading.Models.Rules
{
    internal sealed class CrossoverSnapshotBuilder
    {
        private readonly RuleMarketData data;
        private readonly BoundRuleExecution execution;
        internal CrossoverSnapshotBuilder(RuleMarketData data, BoundRuleExecution execution)
        {
            this.data = data;
            this.execution = execution;
        }

        internal CrossoverCandleSnapshot Capture(CrossoverSubscription subscription, CompletedCandle candle)
        {
            return new CrossoverCandleSnapshot(candle, CaptureValue(subscription.Condition.Left, candle.ClosedAt), CaptureValue(subscription.Condition.Right, candle.ClosedAt));
        }

        private CrossoverValueSnapshot CaptureValue(ValueDefinition value, DateTimeOffset timestamp)
        {
            double calculated = execution.SampleValue(value, timestamp);
            string? timeframe = null;
            CompletedCandle? sourceCandle = null;
            List<CrossoverValueSnapshot> inputs = new List<CrossoverValueSnapshot>();
            switch (value)
            {
                case CandleValue candle:
                    timeframe = candle.Timeframe;
                    sourceCandle = FindCandle(timeframe, timestamp, candle.CandlesAgo);
                    break;
                case IndicatorValue indicator:
                    timeframe = indicator.Timeframe;
                    sourceCandle = FindCandle(timeframe, timestamp, 0);
                    if (sourceCandle != null)
                    {
                        if (indicator.Function == IndicatorFunction.SuperTrend)
                        {
                            inputs.Add(CaptureValue(new CandleValue(timeframe, CandleField.High), sourceCandle.ClosedAt));
                            inputs.Add(CaptureValue(new CandleValue(timeframe, CandleField.Low), sourceCandle.ClosedAt));
                            inputs.Add(CaptureValue(new CandleValue(timeframe, CandleField.Close), sourceCandle.ClosedAt));
                        }
                        else
                        {
                            ValueDefinition? input = indicator.Source;
                            if (input == null)
                            {
                                input = new CandleValue(timeframe);
                            }

                            inputs.Add(CaptureValue(input, sourceCandle.ClosedAt));
                        }
                    }

                    break;
                case ArithmeticValue arithmetic:
                    inputs.Add(CaptureValue(arithmetic.Left, timestamp));
                    inputs.Add(CaptureValue(arithmetic.Right, timestamp));
                    break;
                case RoundValue round:
                    inputs.Add(CaptureValue(round.Source, timestamp));
                    break;
                case PreviousValue previous:
                    timeframe = previous.Timeframe;
                    sourceCandle = FindCandle(timeframe, timestamp, previous.CandlesAgo);
                    if (sourceCandle != null)
                    {
                        inputs.Add(CaptureValue(previous.Source, sourceCandle.ClosedAt));
                    }

                    break;
                case CountValue count:
                    timeframe = count.Timeframe;
                    sourceCandle = FindCandle(timeframe, timestamp, count.CandlesAgo);
                    // Count is reported as an aggregate. Replaying its history here must not emit more events.
                    break;
            }

            IndicatorValue? indicatorDefinition = value as IndicatorValue;
            CandleValue? candleDefinition = value as CandleValue;
            double? multiplier = null;
            IndicatorFunction? function = null;
            int? length = null;
            CandleField? field = null;
            double? result = null;
            if (double.IsFinite(calculated))
            {
                result = calculated;
            }

            if (indicatorDefinition != null)
            {
                function = indicatorDefinition.Function;
                length = indicatorDefinition.Length;
                if (function == IndicatorFunction.BollingerUpperBand || function == IndicatorFunction.BollingerLowerBand || function == IndicatorFunction.SuperTrend)
                {
                    multiplier = indicatorDefinition.Multiplier;
                }
            }

            if (candleDefinition != null)
            {
                field = candleDefinition.Field;
            }

            return new CrossoverValueSnapshot(RuleLanguage.Describe(value), result, timestamp, timeframe, sourceCandle, inputs.AsReadOnly(), function, length, multiplier, field);
        }

        private CompletedCandle? FindCandle(string timeframe, DateTimeOffset timestamp, int candlesAgo)
        {
            CompletedCandle[] candles = data.GetSeries(timeframe);
            int currentIndex = RuleMarketData.FindCompletedIndex(candles, timestamp);
            if (currentIndex < candlesAgo)
            {
                return null;
            }

            return candles[currentIndex - candlesAgo];
        }
    }
}

