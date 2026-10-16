namespace AlgoTrading.Screener.Storage;

public interface ISettingsStore
{
    Task<WindowSettings> LoadAsync(CancellationToken ct = default);
    Task SaveAsync(WindowSettings settings, CancellationToken ct = default);
}