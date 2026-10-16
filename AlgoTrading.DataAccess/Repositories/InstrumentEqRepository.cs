namespace AlgoTrading.DataAccess
{
    using AlgoTrading.DataAccess.Infrastructure.DatabaseContext;
    using AlgoTrading.Models.MarketData.Entities;
    using Microsoft.EntityFrameworkCore;

    namespace Infrastructure.Repositories
    {
        public class InstrumentEqRepository : Repository<InstrumentEq>, IInstrumentEqRepository
        {
            public InstrumentEqRepository(ApplicationDbContext context) : base(context) { }

            public async Task<InstrumentEq?> GetByTokenAndExchangeAsync(long instrumentToken, string exchange)
            {
                return await GetByIdKeysAsync(instrumentToken, exchange);
            }

            public async Task<IEnumerable<InstrumentEq>> GetByExchangeAsync(string exchange)
            {
                return await _dbSet.AsNoTracking()
                                   .Where(x => x.Exchange == exchange)
                                   .ToListAsync();
            }
        }

        public class InstrumentFoRepository : Repository<InstrumentFo>, IInstrumentFoRepository
        {
            public InstrumentFoRepository(ApplicationDbContext context) : base(context) { }

            public async Task<InstrumentFo?> GetByTokenAsync(long instrumentToken)
            {
                return await GetByIdKeysAsync(instrumentToken);
            }

            public async Task<IEnumerable<InstrumentFo>> GetExpiringBeforeAsync(DateTime date)
            {
                return await _dbSet.AsNoTracking()
                                   .Where(x => x.Expiry <= date)
                                   .ToListAsync();
            }
        }
    }
}
