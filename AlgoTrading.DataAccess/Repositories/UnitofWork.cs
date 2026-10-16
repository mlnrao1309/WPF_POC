namespace AlgoTrading.DataAccess
{
    using AlgoTrading.DataAccess.Infrastructure.DatabaseContext;
    using Microsoft.EntityFrameworkCore;
    using System.Linq.Expressions;

    namespace Infrastructure.Repositories
    {
        public interface IUnitOfWork : IDisposable
        {
            IInstrumentEqRepository InstrumentsEq { get; }
            IInstrumentFoRepository InstrumentsFo { get; }
            Task<int> CompleteAsync();
        }

        public class UnitOfWork : IUnitOfWork
        {
            private readonly ApplicationDbContext _context;

            public IInstrumentEqRepository InstrumentsEq { get; }
            public IInstrumentFoRepository InstrumentsFo { get; }

            public UnitOfWork(
                ApplicationDbContext context,
                IInstrumentEqRepository instrumentEqRepository,
                IInstrumentFoRepository instrumentFoRepository)
            {
                _context = context;
                InstrumentsEq = instrumentEqRepository;
                InstrumentsFo = instrumentFoRepository;
            }

            public async Task<int> CompleteAsync()
            {
                return await _context.SaveChangesAsync();
            }

            public void Dispose()
            {
                _context.Dispose();
            }
        }

    }
}
