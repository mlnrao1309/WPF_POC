using AlgoTrading.Models;

namespace AlgoTrading.DataAccess.Calendar
{
    /// <summary>Coordinates download, validation, explicit-day parsing and atomic publication.</summary>
    public sealed class NseTradingCalendarImporter
    {
        private readonly NseHolidaySourceClient sourceClient;
        private readonly NseTradingCalendarParser parser;
        private readonly ITradingCalendarRepository repository;

        public NseTradingCalendarImporter(NseHolidaySourceClient sourceClient, NseTradingCalendarParser parser,
            ITradingCalendarRepository repository)
        {
            this.sourceClient = sourceClient ?? throw new ArgumentNullException(nameof(sourceClient));
            this.parser = parser ?? throw new ArgumentNullException(nameof(parser));
            this.repository = repository ?? throw new ArgumentNullException(nameof(repository));
        }

        public async Task<ExchangeCalendarSnapshot> ImportAsync(NseCalendarImportRequest request,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            HolidaySourceReport report = await sourceClient.FetchAsync(request.SegmentCode, request.Year, cancellationToken);
            ExchangeCalendarSnapshot snapshot = parser.Parse(report, request);

            // Persist every byte of every official input before publishing a revision that depends on it.
            await repository.SaveSourceAsync(report.Source, cancellationToken);
            HashSet<Guid> savedSources = new HashSet<Guid> { report.Source.SourceId };
            foreach (NseSpecialSession special in request.SpecialSessions)
            {
                if (savedSources.Add(special.Source.SourceId))
                {
                    await repository.SaveSourceAsync(special.Source, cancellationToken);
                }
            }
            await repository.PublishAsync(snapshot, cancellationToken);
            return snapshot;
        }
    }
}
