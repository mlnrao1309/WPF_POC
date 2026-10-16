using System;
using System.Collections.Generic;

namespace AlgoTrading.Models
{
    public sealed class HolidaySourceReport
    {
        private readonly CalendarSourceDocument source;
        private readonly string segmentCode;
        private readonly int year;
        private readonly IReadOnlyList<ExchangeHoliday> holidays;

        public HolidaySourceReport(CalendarSourceDocument source, string segmentCode, int year, IReadOnlyList<ExchangeHoliday> holidays)
        {
            if (source == null || holidays == null || string.IsNullOrWhiteSpace(segmentCode))
            {
                throw new ArgumentException("Source, segment and holiday observations are required.");
            }
            holidays = new List<ExchangeHoliday>(holidays).AsReadOnly();
            this.source = source;
            this.segmentCode = segmentCode;
            this.year = year;
            this.holidays = holidays;
        }

        public CalendarSourceDocument Source
        {
            get
            {
                return this.source;
            }
        }

        public string SegmentCode
        {
            get
            {
                return this.segmentCode;
            }
        }

        public int Year
        {
            get
            {
                return this.year;
            }
        }

        public IReadOnlyList<ExchangeHoliday> Holidays
        {
            get
            {
                return this.holidays;
            }
        }
    }
}
