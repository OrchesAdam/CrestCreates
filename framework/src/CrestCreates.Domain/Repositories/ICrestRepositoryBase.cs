using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Domain.Shared.DTOs;

namespace CrestCreates.Domain.Repositories;

public interface ICrestRepositoryBase<TEntity, TKey> where TEntity : class
{
    IQueryable<TEntity> GetQueryable();

    Task<List<TEntity>> GetListAsync(CancellationToken cancellationToken = default);

    Task<List<TEntity>> GetListAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default);

    Task<List<TEntity>> GetListAsync(Expression<Func<TEntity, bool>> predicate, Expression<Func<TEntity, object>> orderBy, bool ascending = true, CancellationToken cancellationToken = default);

    Task<TEntity?> GetAsync(TKey id, CancellationToken cancellationToken = default);

    Task<TEntity?> GetAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default);

    Task<TEntity> InsertAsync(TEntity entity, CancellationToken cancellationToken = default);

    Task<IEnumerable<TEntity>> InsertRangeAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default);

    Task<TEntity> UpdateAsync(TEntity entity, CancellationToken cancellationToken = default);

    Task<IEnumerable<TEntity>> UpdateRangeAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default);

    Task DeleteAsync(TEntity entity, CancellationToken cancellationToken = default);

    Task DeleteAsync(TKey id, CancellationToken cancellationToken = default);

    Task DeleteRangeAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default);

    Task DeleteRangeAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default);

    Task<PagedResult<TEntity>> GetPagedAsync(int pageIndex, int pageSize, CancellationToken cancellationToken = default);

    Task<PagedResult<TEntity>> GetPagedAsync(int pageIndex, int pageSize, Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default);

    Task<PagedResult<TEntity>> GetPagedAsync(int pageIndex, int pageSize, Expression<Func<TEntity, bool>> predicate, Expression<Func<TEntity, object>> orderBy, bool ascending = true, CancellationToken cancellationToken = default);

    Task<PagedResult<TEntity>> GetPagedAsync(int pageIndex, int pageSize, Expression<Func<TEntity, object>> orderBy, bool ascending = true, CancellationToken cancellationToken = default);

    Task<long> GetCountAsync(CancellationToken cancellationToken = default);

    Task<long> GetCountAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default);

    Task<bool> AnyAsync(CancellationToken cancellationToken = default);

    Task<bool> AnyAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(TKey id, CancellationToken cancellationToken = default);
}