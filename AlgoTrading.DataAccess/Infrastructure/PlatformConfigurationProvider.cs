using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AlgoTrading.DataAccess.Infrastructure.DatabaseContext;
using AlgoTrading.Models;
using AlgoTrading.Models.Configuration;
using AlgoTrading.Models.MarketData.Entities;
using Microsoft.EntityFrameworkCore;

namespace AlgoTrading.DataAccess.Infrastructure
{
    /// <summary>Configuration surface consumed at startup and by the hosted pipeline.</summary>
    public interface IPlatformConfigurationProvider
    {
        Task<TimeframeCatalog> GetTimeframeCatalogAsync(CancellationToken cancellationToken = default);

        Task<IReadOnlyList<ConfigCriticalLevel>> GetCriticalLevelRulesAsync(CancellationToken cancellationToken = default);

        Task<PlatformConfigurationSnapshot> LoadActiveConfigurationAsync(
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Loads Config_Timeframes and Config_CriticalLevels. The legacy catalog-only API retains its documented
    /// design-time fallback; runtime activation never falls back when tables are empty or unreachable.
    /// </summary>
    public sealed class PlatformConfigurationProvider : IPlatformConfigurationProvider
    {
        private readonly ApplicationDbContext context;

        public PlatformConfigurationProvider(ApplicationDbContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            this.context = context;
        }

        public async Task<TimeframeCatalog> GetTimeframeCatalogAsync(CancellationToken cancellationToken = default)
        {
            List<ConfigTimeframe> rows = await this.context.ConfigTimeframes.AsNoTracking().ToListAsync(cancellationToken);
            if (rows.Count == 0)
            {
                return TimeframeCatalog.CreateDefault();
            }

            PlatformConfigurationValidator validator = new PlatformConfigurationValidator();
            List<ConfigCriticalLevel> noCriticalLevelRows = new List<ConfigCriticalLevel>();
            return validator.Activate(rows, noCriticalLevelRows).Timeframes;
        }

        public async Task<IReadOnlyList<ConfigCriticalLevel>> GetCriticalLevelRulesAsync(CancellationToken cancellationToken = default)
        {
            List<ConfigCriticalLevel> rows = await this.context.ConfigCriticalLevels.AsNoTracking()
                .Where(row => row.IsActive)
                .ToListAsync(cancellationToken);
            return rows.AsReadOnly();
        }

        public async Task<PlatformConfigurationSnapshot> LoadActiveConfigurationAsync(
            CancellationToken cancellationToken = default)
        {
            try
            {
                List<ConfigTimeframe> timeframeRows = await this.context.ConfigTimeframes.AsNoTracking()
                    .ToListAsync(cancellationToken);
                List<ConfigCriticalLevel> criticalLevelRows = await this.context.ConfigCriticalLevels.AsNoTracking()
                    .ToListAsync(cancellationToken);
                PlatformConfigurationValidator validator = new PlatformConfigurationValidator();
                return validator.Activate(timeframeRows, criticalLevelRows);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (PlatformConfigurationException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new PlatformConfigurationException(
                    "Runtime configuration could not be loaded from Config_Timeframes and Config_CriticalLevels.",
                    exception);
            }
        }
    }
}
