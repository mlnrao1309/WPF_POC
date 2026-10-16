using AlgoTrading.Models.Configuration;

namespace AlgoTrading.DataAccess.Infrastructure
{
    public interface IPlatformConfigurationRuntime
    {
        bool IsInitialized { get; }

        PlatformConfigurationSnapshot Current { get; }

        Task InitializeAsync(IPlatformConfigurationProvider provider,
            CancellationToken cancellationToken = default);
    }
}
