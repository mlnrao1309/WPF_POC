namespace AlgoTrading.Repositories;

public interface IInstrumentRepository
{
    Task<bool> ExistsAsync(long instrumentToken, CancellationToken cancellationToken = default);
}
