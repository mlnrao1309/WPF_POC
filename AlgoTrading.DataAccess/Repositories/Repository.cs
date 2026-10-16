namespace AlgoTrading.DataAccess
{
    using Microsoft.EntityFrameworkCore;
    using System.Linq.Expressions;

    namespace Infrastructure.Repositories
    {
        public class Repository<T> : IRepository<T> where T : class
        {
            protected readonly DbContext _context;
            protected readonly DbSet<T> _dbSet;

            public Repository(DbContext context)
            {
                _context = context;
                _dbSet = context.Set<T>();
            }

            public async Task<T?> GetByIdKeysAsync(params object[] keyValues)
            {
                return await _dbSet.FindAsync(keyValues);
            }

            public async Task<IEnumerable<T>> GetAllAsync()
            {
                return await _dbSet.AsNoTracking().ToListAsync();
            }

            public async Task<IEnumerable<T>> FindAsync(Expression<Func<T, bool>> predicate)
            {
                return await _dbSet.AsNoTracking().Where(predicate).ToListAsync();
            }

            public async Task AddAsync(T entity)
            {
                await _dbSet.AddAsync(entity);
            }

            public async Task AddRangeAsync(IEnumerable<T> entities)
            {
                await _dbSet.AddRangeAsync(entities);
            }

            public void Update(T entity)
            {
                _dbSet.Update(entity);
            }

            public void Remove(T entity)
            {
                _dbSet.Remove(entity);
            }
        }
    }
}