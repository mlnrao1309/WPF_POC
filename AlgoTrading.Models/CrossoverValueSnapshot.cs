namespace AlgoTrading.Models.Rules
{
    /// <summary>A calculated operand and its nested inputs, sampled without looking beyond SampledAt.</summary>
    public sealed class CrossoverValueSnapshot : IEquatable<CrossoverValueSnapshot>
    {
        private string storedDescription;
        private double? storedValue;
        private DateTimeOffset storedSampledAt;
        private string? storedTimeframe;
        private CompletedCandle? storedSourceCandle;
        private IReadOnlyList<CrossoverValueSnapshot> storedInputs;
        private IndicatorFunction? storedIndicatorFunction;
        private int? storedLength;
        private double? storedMultiplier;
        private CandleField? storedCandleField;
        public CrossoverValueSnapshot(string Description, double? Value, DateTimeOffset SampledAt, string? Timeframe, CompletedCandle? SourceCandle, IReadOnlyList<CrossoverValueSnapshot> Inputs, IndicatorFunction? IndicatorFunction = null, int? Length = null, double? Multiplier = null, CandleField? CandleField = null)
        {
            storedDescription = Description;
            storedValue = Value;
            storedSampledAt = SampledAt;
            storedTimeframe = Timeframe;
            storedSourceCandle = SourceCandle;
            storedInputs = Inputs;
            storedIndicatorFunction = IndicatorFunction;
            storedLength = Length;
            storedMultiplier = Multiplier;
            storedCandleField = CandleField;
        }

        public string Description
        {
            get
            {
                return storedDescription;
            }

            init
            {
                storedDescription = value;
            }
        }

        public double? Value
        {
            get
            {
                return storedValue;
            }

            init
            {
                storedValue = value;
            }
        }

        public DateTimeOffset SampledAt
        {
            get
            {
                return storedSampledAt;
            }

            init
            {
                storedSampledAt = value;
            }
        }

        public string? Timeframe
        {
            get
            {
                return storedTimeframe;
            }

            init
            {
                storedTimeframe = value;
            }
        }

        public CompletedCandle? SourceCandle
        {
            get
            {
                return storedSourceCandle;
            }

            init
            {
                storedSourceCandle = value;
            }
        }

        public IReadOnlyList<CrossoverValueSnapshot> Inputs
        {
            get
            {
                return storedInputs;
            }

            init
            {
                storedInputs = value;
            }
        }

        public IndicatorFunction? IndicatorFunction
        {
            get
            {
                return storedIndicatorFunction;
            }

            init
            {
                storedIndicatorFunction = value;
            }
        }

        public int? Length
        {
            get
            {
                return storedLength;
            }

            init
            {
                storedLength = value;
            }
        }

        public double? Multiplier
        {
            get
            {
                return storedMultiplier;
            }

            init
            {
                storedMultiplier = value;
            }
        }

        public CandleField? CandleField
        {
            get
            {
                return storedCandleField;
            }

            init
            {
                storedCandleField = value;
            }
        }

        public bool Equals(CrossoverValueSnapshot? other)
        {
            if (other is null)
            {
                return false;
            }

            return EqualityComparer<string>.Default.Equals(Description, other.Description) && EqualityComparer<double?>.Default.Equals(Value, other.Value) && EqualityComparer<DateTimeOffset>.Default.Equals(SampledAt, other.SampledAt) && EqualityComparer<string?>.Default.Equals(Timeframe, other.Timeframe) && EqualityComparer<CompletedCandle?>.Default.Equals(SourceCandle, other.SourceCandle) && EqualityComparer<IReadOnlyList<CrossoverValueSnapshot>>.Default.Equals(Inputs, other.Inputs) && EqualityComparer<IndicatorFunction?>.Default.Equals(IndicatorFunction, other.IndicatorFunction) && EqualityComparer<int?>.Default.Equals(Length, other.Length) && EqualityComparer<double?>.Default.Equals(Multiplier, other.Multiplier) && EqualityComparer<CandleField?>.Default.Equals(CandleField, other.CandleField);
        }

        public override bool Equals(object? other)
        {
            return Equals(other as CrossoverValueSnapshot);
        }

        public override int GetHashCode()
        {
            HashCode hash = new HashCode();
            hash.Add(typeof(CrossoverValueSnapshot));
            hash.Add(Description);
            hash.Add(Value);
            hash.Add(SampledAt);
            hash.Add(Timeframe);
            hash.Add(SourceCandle);
            hash.Add(Inputs);
            hash.Add(IndicatorFunction);
            hash.Add(Length);
            hash.Add(Multiplier);
            hash.Add(CandleField);
            return hash.ToHashCode();
        }

        public static bool operator ==(CrossoverValueSnapshot? left, CrossoverValueSnapshot? right)
        {
            return object.Equals(left, right);
        }

        public static bool operator !=(CrossoverValueSnapshot? left, CrossoverValueSnapshot? right)
        {
            return !object.Equals(left, right);
        }

        public void Deconstruct(out string Description, out double? Value, out DateTimeOffset SampledAt, out string? Timeframe, out CompletedCandle? SourceCandle, out IReadOnlyList<CrossoverValueSnapshot> Inputs, out IndicatorFunction? IndicatorFunction, out int? Length, out double? Multiplier, out CandleField? CandleField)
        {
            Description = this.Description;
            Value = this.Value;
            SampledAt = this.SampledAt;
            Timeframe = this.Timeframe;
            SourceCandle = this.SourceCandle;
            Inputs = this.Inputs;
            IndicatorFunction = this.IndicatorFunction;
            Length = this.Length;
            Multiplier = this.Multiplier;
            CandleField = this.CandleField;
        }
    }
}

