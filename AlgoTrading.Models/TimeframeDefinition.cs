using System;

namespace AlgoTrading.Models
{
    public sealed class TimeframeDefinition : IEquatable<TimeframeDefinition>
    {
        private readonly string canonicalName;
        private readonly TimeframeKind kind;
        private readonly TimeSpan? duration;

        internal TimeframeDefinition(string canonicalName, TimeframeKind kind, TimeSpan? duration)
        {
            if (string.IsNullOrWhiteSpace(canonicalName))
            {
                throw new ArgumentException("A canonical timeframe name is required.", nameof(canonicalName));
            }
            if (kind == TimeframeKind.Intraday && (!duration.HasValue || duration.Value <= TimeSpan.Zero))
            {
                throw new ArgumentException("An intraday timeframe requires a positive duration.", nameof(duration));
            }
            if (kind != TimeframeKind.Intraday && duration.HasValue)
            {
                throw new ArgumentException("Calendar timeframes cannot carry an intraday duration.", nameof(duration));
            }
            this.canonicalName = canonicalName;
            this.kind = kind;
            this.duration = duration;
        }

        public string CanonicalName
        {
            get
            {
                return this.canonicalName;
            }
        }

        public TimeframeKind Kind
        {
            get
            {
                return this.kind;
            }
        }

        public TimeSpan? Duration
        {
            get
            {
                return this.duration;
            }
        }

        public bool IsCalendarBased
        {
            get
            {
                return this.kind != TimeframeKind.Intraday;
            }
        }

        public bool Equals(TimeframeDefinition? other)
        {
            if (other == null)
            {
                return false;
            }
            return this.canonicalName == other.canonicalName && this.kind == other.kind && this.duration == other.duration;
        }

        public override bool Equals(object? obj)
        {
            return Equals(obj as TimeframeDefinition);
        }

        public override int GetHashCode()
        {
            HashCode hash = new HashCode();
            hash.Add(this.canonicalName);
            hash.Add(this.kind);
            hash.Add(this.duration);
            return hash.ToHashCode();
        }

        public static bool operator ==(TimeframeDefinition? left, TimeframeDefinition? right)
        {
            return object.Equals(left, right);
        }

        public static bool operator !=(TimeframeDefinition? left, TimeframeDefinition? right)
        {
            return !object.Equals(left, right);
        }
    }
}
