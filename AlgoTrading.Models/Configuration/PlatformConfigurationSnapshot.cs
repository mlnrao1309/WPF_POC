namespace AlgoTrading.Models.Configuration
{
    /// <summary>One validated startup view of active timeframes and calculator rules.</summary>
    public sealed class PlatformConfigurationSnapshot
    {
        public PlatformConfigurationSnapshot(TimeframeCatalog timeframes,
            IReadOnlyList<ActiveCriticalLevelRule> criticalLevelRules, string configurationRevision)
        {
            if (timeframes == null)
            {
                throw new ArgumentNullException(nameof(timeframes));
            }

            if (criticalLevelRules == null)
            {
                throw new ArgumentNullException(nameof(criticalLevelRules));
            }

            if (string.IsNullOrWhiteSpace(configurationRevision))
            {
                throw new ArgumentException("A configuration revision is required.", nameof(configurationRevision));
            }

            this.Timeframes = timeframes;
            this.CriticalLevelRules = criticalLevelRules;
            this.ConfigurationRevision = configurationRevision;
        }

        public TimeframeCatalog Timeframes { get; }

        public IReadOnlyList<ActiveCriticalLevelRule> CriticalLevelRules { get; }

        public string ConfigurationRevision { get; }
    }
}
