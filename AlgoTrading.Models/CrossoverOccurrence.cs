namespace AlgoTrading.Models.Rules
{
    /// <summary>Immutable event context. Next is optional and never used to decide whether a crossover occurred.</summary>
    public sealed class CrossoverOccurrence : IEquatable<CrossoverOccurrence>
    {
        private Guid storedEventId;
        private string storedRuleName;
        private string storedConditionPath;
        private string storedConditionDescription;
        private int storedInstrumentToken;
        private string storedEvaluationTimeframe;
        private ComparisonOperator storedDirection;
        private DateTimeOffset storedOccurredAt;
        private DateTimeOffset storedObservedAt;
        private CrossoverCandleSnapshot storedPrevious;
        private CrossoverCandleSnapshot storedCurrent;
        private CrossoverCandleSnapshot? storedNext;
        private RuleEvaluation storedOverallRuleEvaluation;
        public CrossoverOccurrence(Guid EventId, string RuleName, string ConditionPath, string ConditionDescription, int InstrumentToken, string EvaluationTimeframe, ComparisonOperator Direction, DateTimeOffset OccurredAt, DateTimeOffset ObservedAt, CrossoverCandleSnapshot Previous, CrossoverCandleSnapshot Current, CrossoverCandleSnapshot? Next, RuleEvaluation OverallRuleEvaluation)
        {
            storedEventId = EventId;
            storedRuleName = RuleName;
            storedConditionPath = ConditionPath;
            storedConditionDescription = ConditionDescription;
            storedInstrumentToken = InstrumentToken;
            storedEvaluationTimeframe = EvaluationTimeframe;
            storedDirection = Direction;
            storedOccurredAt = OccurredAt;
            storedObservedAt = ObservedAt;
            storedPrevious = Previous;
            storedCurrent = Current;
            storedNext = Next;
            storedOverallRuleEvaluation = OverallRuleEvaluation;
        }

        public Guid EventId
        {
            get
            {
                return storedEventId;
            }

            init
            {
                storedEventId = value;
            }
        }

        public string RuleName
        {
            get
            {
                return storedRuleName;
            }

            init
            {
                storedRuleName = value;
            }
        }

        public string ConditionPath
        {
            get
            {
                return storedConditionPath;
            }

            init
            {
                storedConditionPath = value;
            }
        }

        public string ConditionDescription
        {
            get
            {
                return storedConditionDescription;
            }

            init
            {
                storedConditionDescription = value;
            }
        }

        public int InstrumentToken
        {
            get
            {
                return storedInstrumentToken;
            }

            init
            {
                storedInstrumentToken = value;
            }
        }

        public string EvaluationTimeframe
        {
            get
            {
                return storedEvaluationTimeframe;
            }

            init
            {
                storedEvaluationTimeframe = value;
            }
        }

        public ComparisonOperator Direction
        {
            get
            {
                return storedDirection;
            }

            init
            {
                storedDirection = value;
            }
        }

        public DateTimeOffset OccurredAt
        {
            get
            {
                return storedOccurredAt;
            }

            init
            {
                storedOccurredAt = value;
            }
        }

        public DateTimeOffset ObservedAt
        {
            get
            {
                return storedObservedAt;
            }

            init
            {
                storedObservedAt = value;
            }
        }

        public CrossoverCandleSnapshot Previous
        {
            get
            {
                return storedPrevious;
            }

            init
            {
                storedPrevious = value;
            }
        }

        public CrossoverCandleSnapshot Current
        {
            get
            {
                return storedCurrent;
            }

            init
            {
                storedCurrent = value;
            }
        }

        public CrossoverCandleSnapshot? Next
        {
            get
            {
                return storedNext;
            }

            init
            {
                storedNext = value;
            }
        }

        public RuleEvaluation OverallRuleEvaluation
        {
            get
            {
                return storedOverallRuleEvaluation;
            }

            init
            {
                storedOverallRuleEvaluation = value;
            }
        }

        public bool Equals(CrossoverOccurrence? other)
        {
            if (other is null)
            {
                return false;
            }

            return EqualityComparer<Guid>.Default.Equals(EventId, other.EventId) && EqualityComparer<string>.Default.Equals(RuleName, other.RuleName) && EqualityComparer<string>.Default.Equals(ConditionPath, other.ConditionPath) && EqualityComparer<string>.Default.Equals(ConditionDescription, other.ConditionDescription) && EqualityComparer<int>.Default.Equals(InstrumentToken, other.InstrumentToken) && EqualityComparer<string>.Default.Equals(EvaluationTimeframe, other.EvaluationTimeframe) && EqualityComparer<ComparisonOperator>.Default.Equals(Direction, other.Direction) && EqualityComparer<DateTimeOffset>.Default.Equals(OccurredAt, other.OccurredAt) && EqualityComparer<DateTimeOffset>.Default.Equals(ObservedAt, other.ObservedAt) && EqualityComparer<CrossoverCandleSnapshot>.Default.Equals(Previous, other.Previous) && EqualityComparer<CrossoverCandleSnapshot>.Default.Equals(Current, other.Current) && EqualityComparer<CrossoverCandleSnapshot?>.Default.Equals(Next, other.Next) && EqualityComparer<RuleEvaluation>.Default.Equals(OverallRuleEvaluation, other.OverallRuleEvaluation);
        }

        public override bool Equals(object? other)
        {
            return Equals(other as CrossoverOccurrence);
        }

        public override int GetHashCode()
        {
            HashCode hash = new HashCode();
            hash.Add(typeof(CrossoverOccurrence));
            hash.Add(EventId);
            hash.Add(RuleName);
            hash.Add(ConditionPath);
            hash.Add(ConditionDescription);
            hash.Add(InstrumentToken);
            hash.Add(EvaluationTimeframe);
            hash.Add(Direction);
            hash.Add(OccurredAt);
            hash.Add(ObservedAt);
            hash.Add(Previous);
            hash.Add(Current);
            hash.Add(Next);
            hash.Add(OverallRuleEvaluation);
            return hash.ToHashCode();
        }

        public static bool operator ==(CrossoverOccurrence? left, CrossoverOccurrence? right)
        {
            return object.Equals(left, right);
        }

        public static bool operator !=(CrossoverOccurrence? left, CrossoverOccurrence? right)
        {
            return !object.Equals(left, right);
        }

        public void Deconstruct(out Guid EventId, out string RuleName, out string ConditionPath, out string ConditionDescription, out int InstrumentToken, out string EvaluationTimeframe, out ComparisonOperator Direction, out DateTimeOffset OccurredAt, out DateTimeOffset ObservedAt, out CrossoverCandleSnapshot Previous, out CrossoverCandleSnapshot Current, out CrossoverCandleSnapshot? Next, out RuleEvaluation OverallRuleEvaluation)
        {
            EventId = this.EventId;
            RuleName = this.RuleName;
            ConditionPath = this.ConditionPath;
            ConditionDescription = this.ConditionDescription;
            InstrumentToken = this.InstrumentToken;
            EvaluationTimeframe = this.EvaluationTimeframe;
            Direction = this.Direction;
            OccurredAt = this.OccurredAt;
            ObservedAt = this.ObservedAt;
            Previous = this.Previous;
            Current = this.Current;
            Next = this.Next;
            OverallRuleEvaluation = this.OverallRuleEvaluation;
        }

        internal CrossoverOccurrence WithNextCandle(DateTimeOffset observedAt, CrossoverCandleSnapshot next)
        {
            return new CrossoverOccurrence(EventId, RuleName, ConditionPath, ConditionDescription, InstrumentToken, EvaluationTimeframe, Direction, OccurredAt, observedAt, Previous, Current, next, OverallRuleEvaluation);
        }
    }
}

