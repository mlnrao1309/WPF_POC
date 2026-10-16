using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using YourApp.Repositories;

namespace YourApp.Services;

public class MarketWatchExpiryService : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(5);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<MarketWatchExpiryService> _logger;

    public MarketWatchExpiryService(
        IServiceScopeFactory scopeFactory,
        ILogger<MarketWatchExpiryService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Small startup delay so the UI/window has time to appear
        try { await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken); }
        catch (TaskCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var repo = scope.ServiceProvider.GetRequiredService<IMarketWatchRepository>();

                var affected = await repo.PurgeExpiredAsync(DateTime.UtcNow, stoppingToken);
                if (affected > 0)
                    _logger.LogInformation(
                        "MarketWatchExpiryService soft-deleted {Count} expired row(s).", affected);
            }
            catch (OperationCanceledException) { /* shutdown */ }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while purging expired MarketWatch rows.");
            }

            try { await Task.Delay(PollInterval, stoppingToken); }
            catch (TaskCanceledException) { break; }
        }
    }
}