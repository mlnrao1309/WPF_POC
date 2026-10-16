namespace AlgoTrading.Services;

public interface IHistoricalPollingCoordinator
{
    Task RunAsync(IReadOnlyCollection<long> instrumentTokens, IReadOnlyCollection<string> intervals,
        IProgress<string>? progress = null, CancellationToken cancellationToken = default);
}
