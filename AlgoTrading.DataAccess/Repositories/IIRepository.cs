namespace AlgoTrading.DataAccess
{
    using System.Linq.Expressions;

    namespace Infrastructure.Repositories
    {
        public interface IRepository<T> where T : class
        {
            Task<T?> GetByIdKeysAsync(params object[] keyValues);
            Task<IEnumerable<T>> GetAllAsync();
            Task<IEnumerable<T>> FindAsync(Expression<Func<T, bool>> predicate);
            Task AddAsync(T entity);
            Task AddRangeAsync(IEnumerable<T> entities);
            void Update(T entity);
            void Remove(T entity);
        }
    }
}
