using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.DbContextProvider.Abstract;
using CrestCreates.Domain.Entities;
using CrestCreates.Domain.Repositories;
using CrestCreates.Domain.Shared.Entities;
using CrestCreates.OrmProviders.EFCore.DbContexts;

namespace CrestCreates.OrmProviders.EFCore.Repositories
{
    public class EfCoreRepository<TEntity, TId> : CrestRepositoryBase<TEntity, TId>
        where TEntity : class, IEntity<TId>
        where TId : IEquatable<TId>
    {
        private readonly IDataBaseContext _dbContext;

        public EfCoreRepository(IDataBaseContext dbContext)
        {
            _dbContext = dbContext;
        }

        public override IQueryable<TEntity> GetQueryable()
        {
            return GetNativeQueryable(_dbContext.Queryable<TEntity>().GetNativeQuery());
        }

        public override IQueryable<TEntity> GetQueryableUnfiltered()
        {
            return GetNativeQueryable(_dbContext.Queryable<TEntity>().IgnoreQueryFilters().GetNativeQuery());
        }

        public override async Task<List<TEntity>> GetListAsync(CancellationToken cancellationToken = default)
        {
            return await _dbContext.Queryable<TEntity>().ToListAsync(cancellationToken);
        }

        public override async Task<List<TEntity>> GetListAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
        {
            return await _dbContext.Queryable<TEntity>().Where(predicate).ToListAsync(cancellationToken);
        }

        public override async Task<List<TEntity>> GetListAsync(Expression<Func<TEntity, bool>> predicate, Expression<Func<TEntity, object>> orderBy, bool ascending = true, CancellationToken cancellationToken = default)
        {
            var query = _dbContext.Queryable<TEntity>().Where(predicate);
            query = ascending ? query.OrderBy(orderBy) : query.OrderByDescending(orderBy);
            return await query.ToListAsync(cancellationToken);
        }

        public override async Task<TEntity?> GetAsync(TId id, CancellationToken cancellationToken = default)
        {
            return await _dbContext.Queryable<TEntity>().Where(e => e.Id.Equals(id)).FirstOrDefaultAsync(cancellationToken);
        }

        public override async Task<TEntity?> GetAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
        {
            return await _dbContext.Queryable<TEntity>().Where(predicate).FirstOrDefaultAsync(cancellationToken);
        }

        public override async Task<TEntity> InsertAsync(TEntity entity, CancellationToken cancellationToken = default)
        {
            await _dbContext.Set<TEntity>().AddAsync(entity, cancellationToken);
            await SaveChangesIfNoActiveTransactionAsync(cancellationToken);
            return entity;
        }

        public override async Task<IEnumerable<TEntity>> InsertRangeAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
        {
            await _dbContext.Set<TEntity>().AddRangeAsync(entities, cancellationToken);
            await SaveChangesIfNoActiveTransactionAsync(cancellationToken);
            return entities;
        }

        public override async Task<TEntity> UpdateAsync(TEntity entity, CancellationToken cancellationToken = default)
        {
            _dbContext.Set<TEntity>().Update(entity);
            await SaveChangesIfNoActiveTransactionAsync(cancellationToken);
            return entity;
        }

        public override async Task<IEnumerable<TEntity>> UpdateRangeAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
        {
            _dbContext.Set<TEntity>().UpdateRange(entities);
            await SaveChangesIfNoActiveTransactionAsync(cancellationToken);
            return entities;
        }

        public override async Task DeleteAsync(TEntity entity, CancellationToken cancellationToken = default)
        {
            _dbContext.Set<TEntity>().Remove(entity);
            await SaveChangesIfNoActiveTransactionAsync(cancellationToken);
        }

        public override async Task DeleteAsync(TId id, CancellationToken cancellationToken = default)
        {
            var entity = await GetAsync(id, cancellationToken);
            if (entity != null)
            {
                _dbContext.Set<TEntity>().Remove(entity);
                await SaveChangesIfNoActiveTransactionAsync(cancellationToken);
            }
        }

        public override async Task DeleteRangeAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
        {
            _dbContext.Set<TEntity>().RemoveRange(entities);
            await SaveChangesIfNoActiveTransactionAsync(cancellationToken);
        }

        public override async Task DeleteRangeAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
        {
            var entities = await _dbContext.Queryable<TEntity>().Where(predicate).ToListAsync(cancellationToken);
            _dbContext.Set<TEntity>().RemoveRange(entities);
            await SaveChangesIfNoActiveTransactionAsync(cancellationToken);
        }

        public override async Task<Domain.Shared.DTOs.PagedResult<TEntity>> GetPagedAsync(int pageIndex, int pageSize, CancellationToken cancellationToken = default)
        {
            var result = await _dbContext.Queryable<TEntity>().ToPagedResultAsync(pageIndex, pageSize, cancellationToken);
            return new Domain.Shared.DTOs.PagedResult<TEntity>(result.Items, (int)result.TotalCount, pageIndex, pageSize);
        }

        public override async Task<Domain.Shared.DTOs.PagedResult<TEntity>> GetPagedAsync(int pageIndex, int pageSize, Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
        {
            var result = await _dbContext.Queryable<TEntity>().Where(predicate).ToPagedResultAsync(pageIndex, pageSize, cancellationToken);
            return new Domain.Shared.DTOs.PagedResult<TEntity>(result.Items, (int)result.TotalCount, pageIndex, pageSize);
        }

        public override async Task<Domain.Shared.DTOs.PagedResult<TEntity>> GetPagedAsync(int pageIndex, int pageSize, Expression<Func<TEntity, bool>> predicate, Expression<Func<TEntity, object>> orderBy, bool ascending = true, CancellationToken cancellationToken = default)
        {
            var query = _dbContext.Queryable<TEntity>().Where(predicate);
            query = ascending ? query.OrderBy(orderBy) : query.OrderByDescending(orderBy);
            var result = await query.ToPagedResultAsync(pageIndex, pageSize, cancellationToken);
            return new Domain.Shared.DTOs.PagedResult<TEntity>(result.Items, (int)result.TotalCount, pageIndex, pageSize);
        }

        public override async Task<Domain.Shared.DTOs.PagedResult<TEntity>> GetPagedAsync(int pageIndex, int pageSize, Expression<Func<TEntity, object>> orderBy, bool ascending = true, CancellationToken cancellationToken = default)
        {
            var query = _dbContext.Queryable<TEntity>();
            query = ascending ? query.OrderBy(orderBy) : query.OrderByDescending(orderBy);
            var result = await query.ToPagedResultAsync(pageIndex, pageSize, cancellationToken);
            return new Domain.Shared.DTOs.PagedResult<TEntity>(result.Items, (int)result.TotalCount, pageIndex, pageSize);
        }

        public override async Task<long> GetCountAsync(CancellationToken cancellationToken = default)
        {
            return await _dbContext.Queryable<TEntity>().LongCountAsync(cancellationToken);
        }

        public override async Task<long> GetCountAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
        {
            return await _dbContext.Queryable<TEntity>().Where(predicate).LongCountAsync(cancellationToken);
        }

        public override async Task<bool> AnyAsync(CancellationToken cancellationToken = default)
        {
            return await _dbContext.Queryable<TEntity>().AnyAsync(cancellationToken);
        }

        public override async Task<bool> AnyAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
        {
            return await _dbContext.Queryable<TEntity>().AnyAsync(predicate, cancellationToken);
        }

        public override async Task<bool> ExistsAsync(TId id, CancellationToken cancellationToken = default)
        {
            return await _dbContext.Queryable<TEntity>().AnyAsync(e => e.Id.Equals(id), cancellationToken);
        }

        private async Task SaveChangesIfNoActiveTransactionAsync(CancellationToken cancellationToken)
        {
            if (_dbContext.CurrentTransaction == null)
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
        }

        private static IQueryable<TEntity> GetNativeQueryable(object nativeQuery)
        {
            return nativeQuery as IQueryable<TEntity>
                ?? throw new InvalidOperationException(
                    $"EF Core native query for {typeof(TEntity).FullName} must be IQueryable<{typeof(TEntity).Name}>.");
        }
    }
}
