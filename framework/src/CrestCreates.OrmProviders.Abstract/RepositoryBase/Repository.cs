using System.Linq.Expressions;
using CrestCreates.DbContextProvider.Abstract;
using CrestCreates.Domain.Entities;
using CrestCreates.OrmProviders.Abstract.Abstractions;

namespace CrestCreates.OrmProviders.Abstract.RepositoryBase;

public abstract class Repository<TEntity, TId> : IRepository<TEntity, TId> 
    where TEntity : class, IEntity<TId>
    where TId : IEquatable<TId>
{
    private readonly IQueryableBuilder<TEntity> _queryableBuilder;
    private readonly IDataBaseSet<TEntity> _dataBaseSet;

    protected Repository(IDataBaseContext dbContext)
    {
        _dataBaseSet = dbContext.Set<TEntity>();
        _queryableBuilder = dbContext.Queryable<TEntity>();
    }

    public async Task<List<TEntity>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _queryableBuilder.ToListAsync(cancellationToken);
    }

    public async Task<List<TEntity>> FindAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
    {
        return await _queryableBuilder.Where(predicate).ToListAsync(cancellationToken);
    }

    public async Task<TEntity> FirstOrDefaultAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
    {
        return await _queryableBuilder.Where(predicate).FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<bool> AnyAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
    {
        return await _queryableBuilder.AnyAsync(predicate, cancellationToken);
    }

    public async Task<int> CountAsync(Expression<Func<TEntity, bool>> predicate = null, CancellationToken cancellationToken = default)
    {
        return await _queryableBuilder.Where(predicate).CountAsync(cancellationToken);
    }

    public async Task<long> LongCountAsync(Expression<Func<TEntity, bool>> predicate = null, CancellationToken cancellationToken = default)
    {
        return await _queryableBuilder.Where(predicate).LongCountAsync(cancellationToken);
    }

    public async Task<TEntity> AddAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        return await _dataBaseSet.AddAsync(entity, cancellationToken);
    }

    public async Task AddRangeAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
    {
        await _dataBaseSet.AddRangeAsync(entities, cancellationToken);
    }

    public async Task<TEntity> UpdateAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        return await _dataBaseSet.UpdateAsync(entity, cancellationToken);
    }

    public async Task<int> UpdateRangeAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
    {
        return await _dataBaseSet.UpdateRangeAsync(entities, cancellationToken);
    }

    public async Task DeleteAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        await _dataBaseSet.RemoveAsync(entity, cancellationToken);
    }

    public async Task<int> DeleteRangeAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
    {
        var enumerable = entities.ToList();
        _dataBaseSet.RemoveRange(enumerable);
        return await Task.FromResult(enumerable.Count);
    }

    public async Task<int> DeleteAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
    {
        return await _dataBaseSet.RemoveRangeAsync(predicate, cancellationToken);
    }

    public IQueryableBuilder<TEntity> AsQueryable()
    {
        return _queryableBuilder;
    }

    public async Task<TEntity> GetByIdAsync(TId id, CancellationToken cancellationToken = default)
    {
        return await _queryableBuilder.Where(x => x.Id.Equals(id)).FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<List<TEntity>> GetByIdsAsync(IEnumerable<TId> ids, CancellationToken cancellationToken = default)
    {
        return await _queryableBuilder.Where(x => ids.Contains(x.Id)).ToListAsync(cancellationToken);
    }

    public async Task DeleteByIdAsync(TId id, CancellationToken cancellationToken = default)
    {
        await DeleteAsync(x => x.Id.Equals(id), cancellationToken);
    }

    public async Task<int> DeleteByIdsAsync(IEnumerable<TId> ids, CancellationToken cancellationToken = default)
    {
        return await DeleteAsync(x => ids.Contains(x.Id), cancellationToken);
    }
}