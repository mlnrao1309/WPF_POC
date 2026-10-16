using AlgoTrading.Models.Configuration;

namespace AlgoTrading.DataAccess.Infrastructure
{
    /// <summary>Thread-safe startup boundary consumed by aggregation and calculation coordinators.</summary>
    public sealed class PlatformConfigurationRuntime : IPlatformConfigurationRuntime
    {
        private readonly SemaphoreSlim initializationGate;
        private PlatformConfigurationSnapshot? current;

        public PlatformConfigurationRuntime()
        {
            this.initializationGate = new SemaphoreSlim(1, 1);
        }

        public bool IsInitialized
        {
            get
            {
                return Volatile.Read(ref this.current) != null;
            }
        }

        public PlatformConfigurationSnapshot Current
        {
            get
            {
                PlatformConfigurationSnapshot? snapshot = Volatile.Read(ref this.current);
                if (snapshot == null)
                {
                    throw new PlatformConfigurationException(
                        "Runtime configuration has not been initialized. Background processing is not ready.");
                }

                return snapshot;
            }
        }

        public async Task InitializeAsync(IPlatformConfigurationProvider provider,
            CancellationToken cancellationToken = default)
        {
            if (provider == null)
            {
                throw new ArgumentNullException(nameof(provider));
            }

            await this.initializationGate.WaitAsync(cancellationToken);
            try
            {
                PlatformConfigurationSnapshot snapshot = await provider.LoadActiveConfigurationAsync(cancellationToken);
                Volatile.Write(ref this.current, snapshot);
            }
            finally
            {
                this.initializationGate.Release();
            }
        }
    }
}
