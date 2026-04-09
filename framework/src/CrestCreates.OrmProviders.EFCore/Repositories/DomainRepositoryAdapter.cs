using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Domain.Repositories;

namespace CrestCreates.OrmProviders.EFCore.Repositories;

public class DomainRepositoryAdapter<TEntity, TKey> : IRepository<TEntity, TKey>
    where TEntity : class
{
    private readonly ICrestRepositoryBase<TEntity, TKey> _repository;

    public DomainRepositoryAdapter(ICrestRepositoryBase<TEntity, TKey> repository)
    {
        _repository = repository;
    }

    public Task<TEntity?> GetByIdAsync(TKey id, CancellationToken cancellationToken = default)
    {
        return _repository.GetAsync(id, cancellationToken);
    }

    public Task<List<TEntity>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return _repository.GetListAsync(cancellationToken);
    }

    public Task<TEntity> AddAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        return _repository.InsertAsync(entity, cancellationToken);
    }

    public Task<TEntity> UpdateAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        return _repository.UpdateAsync(entity, cancellationToken);
    }

    public Task DeleteAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        return _repository.DeleteAsync(entity, cancellationToken);
    }

    public Task<List<TEntity>> FindAsync(
        Expression<Func<TEntity, bool>> predicate,
        CancellationToken cancellationToken = default)
    {
        return _repository.GetListAsync(predicate, cancellationToken);
    }
}
