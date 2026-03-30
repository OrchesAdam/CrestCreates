using CrestCreates.DbContextProvider.Abstract;
using CrestCreates.Domain.Entities;
using CrestCreates.OrmProviders.Abstract.RepositoryBase;
using CrestCreates.OrmProviders.EFCore.DbContexts;

namespace CrestCreates.OrmProviders.EFCore.Repositories
{
    public class EfCoreRepository<TEntity, TId> : Repository<TEntity, TId>
    where TEntity : class, IEntity<TId>
    where TId : IEquatable<TId>
    {
        private readonly CrestCreatesDbContext _dbContext;

        public EfCoreRepository(IDataBaseContext dbContext, CrestCreatesDbContext dbContext2) : base(dbContext)
        {
            _dbContext = dbContext2;
        }
    }
}