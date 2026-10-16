namespace AlgoTrading.DataAccess
{
    using System.Linq.Expressions;
    using AlgoTrading.Models.MarketData.Entities;

    namespace Infrastructure.Repositories
    {
        public interface IInstrumentEqRepository : IRepository<InstrumentEq>
        {
            Task<InstrumentEq?> GetByTokenAndExchangeAsync(long instrumentToken, string exchange);
            Task<IEnumerable<InstrumentEq>> GetByExchangeAsync(string exchange);
        }

        public interface IInstrumentFoRepository : IRepository<InstrumentFo>
        {
            Task<InstrumentFo?> GetByTokenAsync(long instrumentToken);
            Task<IEnumerable<InstrumentFo>> GetExpiringBeforeAsync(DateTime date);
        }
    }
}
