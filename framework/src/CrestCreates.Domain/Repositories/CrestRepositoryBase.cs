using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Domain.DataFilter;
using CrestCreates.Domain.Entities.Auditing;
using CrestCreates.Domain.Shared.DTOs;
using CrestCreates.MultiTenancy.Abstract;

namespace CrestCreates.Domain.Repositories;

public class TenantFilter { }

public abstract class CrestRepositoryBase<TEntity, TKey> : ICrestRepositoryBase<TEntity, TKey>
    where TEntity : class
{
    protected ICurrentTenant? CurrentTenant { get; set; }
    protected DataFilterState? DataFilterState { get; set; }

    public abstract IQueryable<TEntity> GetQueryableUnfiltered();

    public virtual IQueryable<TEntity> GetQueryable()
    {
        var query = GetQueryableUnfiltered();

        if (DataFilterState?.IsEnabled<TenantFilter>() == true && CurrentTenant != null)
        {
            if (typeof(IMustHaveTenant).IsAssignableFrom(typeof(TEntity)))
            {
                var tenantId = CurrentTenant.Id;
                query = query.Where(e => ((IMustHaveTenant)e).TenantId == tenantId);
            }
        }

        return query;
    }

    public abstract Task<List<TEntity>> GetListAsync(CancellationToken cancellationToken = default);

    public abstract Task<List<TEntity>> GetListAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default);

    public abstract Task<List<TEntity>> GetListAsync(Expression<Func<TEntity, bool>> predicate, Expression<Func<TEntity, object>> orderBy, bool ascending = true, CancellationToken cancellationToken = default);

    public abstract Task<TEntity?> GetAsync(TKey id, CancellationToken cancellationToken = default);

    public abstract Task<TEntity?> GetAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default);

    public abstract Task<TEntity> InsertAsync(TEntity entity, CancellationToken cancellationToken = default);

    public abstract Task<IEnumerable<TEntity>> InsertRangeAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default);

    public abstract Task<TEntity> UpdateAsync(TEntity entity, CancellationToken cancellationToken = default);

    public abstract Task<IEnumerable<TEntity>> UpdateRangeAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default);

    public abstract Task DeleteAsync(TEntity entity, CancellationToken cancellationToken = default);

    public abstract Task DeleteAsync(TKey id, CancellationToken cancellationToken = default);

    public abstract Task DeleteRangeAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default);

    public abstract Task DeleteRangeAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default);

    public abstract Task<PagedResult<TEntity>> GetPagedAsync(int pageIndex, int pageSize, CancellationToken cancellationToken = default);

    public abstract Task<PagedResult<TEntity>> GetPagedAsync(int pageIndex, int pageSize, Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default);

    public abstract Task<PagedResult<TEntity>> GetPagedAsync(int pageIndex, int pageSize, Expression<Func<TEntity, bool>> predicate, Expression<Func<TEntity, object>> orderBy, bool ascending = true, CancellationToken cancellationToken = default);

    public abstract Task<PagedResult<TEntity>> GetPagedAsync(int pageIndex, int pageSize, Expression<Func<TEntity, object>> orderBy, bool ascending = true, CancellationToken cancellationToken = default);

    public abstract Task<long> GetCountAsync(CancellationToken cancellationToken = default);

    public abstract Task<long> GetCountAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default);

    public abstract Task<bool> AnyAsync(CancellationToken cancellationToken = default);

    public abstract Task<bool> AnyAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default);

    public abstract Task<bool> ExistsAsync(TKey id, CancellationToken cancellationToken = default);
}
