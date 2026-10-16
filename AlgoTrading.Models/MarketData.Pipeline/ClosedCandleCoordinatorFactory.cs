using AlgoTrading.Models.Configuration;
using AlgoTrading.Models.MarketData.Processing;

namespace AlgoTrading.Models.MarketData.Pipeline
{
    /// <summary>Creates the Batch 3 processor from an already selected configuration and hydrated calendars.</summary>
    public sealed class ClosedCandleCoordinatorFactory : IInstrumentCandleProcessorFactory
    {
        private readonly IReadOnlyDictionary<int, ITradingSessionCalendar> calendars;
        private readonly PlatformConfigurationSnapshot configuration;

        public ClosedCandleCoordinatorFactory(IReadOnlyDictionary<int, ITradingSessionCalendar> calendars,
            PlatformConfigurationSnapshot configuration)
        {
            if (calendars == null)
            {
                throw new ArgumentNullException(nameof(calendars));
            }

            if (configuration == null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            this.calendars = calendars;
            this.configuration = configuration;
        }

        public IClosedCandleEventProcessor Create(int instrumentToken)
        {
            ITradingSessionCalendar? calendar;
            if (!this.calendars.TryGetValue(instrumentToken, out calendar) || calendar == null)
            {
                throw new KeyNotFoundException("No hydrated selected calendar exists for instrument "
                    + instrumentToken + ".");
            }

            return new ClosedCandleProcessingCoordinator(instrumentToken, calendar, this.configuration);
        }
    }
}
