using AlgoTrading.Models;

namespace AlgoTrading.DataAccess.Calendar
{
    public sealed class NseCalendarImportRequest
    {
        public NseCalendarImportRequest(string segmentCode, int year, string revision,
            TimeOnly regularOpen, TimeOnly regularClose,
            IReadOnlyList<NseSpecialSession>? specialSessions = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(segmentCode);
            ArgumentException.ThrowIfNullOrWhiteSpace(revision);
            if (year < 2000 || year > 9998)
            {
                throw new ArgumentOutOfRangeException(nameof(year));
            }
            if (regularOpen >= regularClose)
            {
                throw new ArgumentException("Regular close must follow regular open.");
            }

            SegmentCode = segmentCode;
            Year = year;
            Revision = revision;
            RegularOpen = regularOpen;
            RegularClose = regularClose;
            SpecialSessions = specialSessions == null
                ? Array.Empty<NseSpecialSession>()
                : new List<NseSpecialSession>(specialSessions).AsReadOnly();
        }

        public string SegmentCode { get; }
        public int Year { get; }
        public string Revision { get; }
        public TimeOnly RegularOpen { get; }
        public TimeOnly RegularClose { get; }
        public IReadOnlyList<NseSpecialSession> SpecialSessions { get; }
    }
}
