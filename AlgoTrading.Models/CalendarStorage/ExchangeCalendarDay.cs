using System;
using System.Collections.Generic;

namespace AlgoTrading.Models
{
    public sealed class ExchangeCalendarDay
    {
        private readonly DateOnly date;
        private readonly TimeOnly? opensAt;
        private readonly TimeOnly? closesAt;
        private readonly string reason;

        public ExchangeCalendarDay(DateOnly date, TimeOnly? opensAt, TimeOnly? closesAt, string reason)
        {
            if (opensAt.HasValue != closesAt.HasValue)
            {
                throw new ArgumentException("Provide both session times or neither for a closed day.");
            }
            if (opensAt.HasValue && closesAt.HasValue && opensAt.Value >= closesAt.Value)
            {
                throw new ArgumentException("Session close must follow open within the IST date.");
            }
            if (string.IsNullOrWhiteSpace(reason) || reason.Length > 256)
            {
                throw new ArgumentException("An explicit session or closure reason is required.");
            }
            this.date = date;
            this.opensAt = opensAt;
            this.closesAt = closesAt;
            this.reason = reason;
        }

        public DateOnly Date
        {
            get
            {
                return this.date;
            }
        }

        public TimeOnly? OpensAt
        {
            get
            {
                return this.opensAt;
            }
        }

        public TimeOnly? ClosesAt
        {
            get
            {
                return this.closesAt;
            }
        }

        public string Reason
        {
            get
            {
                return this.reason;
            }
        }
    }
}
