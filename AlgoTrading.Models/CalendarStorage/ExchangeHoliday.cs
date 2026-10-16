using System;
using System.Collections.Generic;

namespace AlgoTrading.Models
{
    public sealed class ExchangeHoliday
    {
        private readonly DateOnly date;
        private readonly string description;

        public ExchangeHoliday(DateOnly date, string description)
        {
            this.date = date;
            this.description = description;
        }

        public DateOnly Date
        {
            get
            {
                return this.date;
            }
        }

        public string Description
        {
            get
            {
                return this.description;
            }
        }
    }
}
