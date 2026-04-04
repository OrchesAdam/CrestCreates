using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Domain.DataFilter;
using CrestCreates.Domain.Entities;
using CrestCreates.Domain.Repositories;
using CrestCreates.Domain.Shared.DTOs;
using CrestCreates.MultiTenancy.Abstract;
using Microsoft.EntityFrameworkCore;
using IDbContextProvider = CrestCreates.DbContextProvider.Abstract;

namespace CrestCreates.OrmProviders.EFCore.Repositories
{
    public abstract class EfCoreRepositoryBase<TEntity, TKey> : CrestRepositoryBase<TEntity, TKey>
        where TEntity : class, IEntity<TKey>
        where TKey : IEquatable<TKey>
    {
        protected readonly IDbContextProvider.IDataBaseContext _dbContext;

        public EfCoreRepositoryBase(
            IDbContextProvider.IDataBaseContext dbContext,
            ICurrentTenant currentTenant,
            DataFilterState dataFilterState)
        {
            _dbContext = dbContext;
            CurrentTenant = currentTenant;
            DataFilterState = dataFilterState;
        }

        public override IQueryable<TEntity> GetQueryableUnfiltered()
        {
            return _dbContext.Queryable<TEntity>().GetNativeQuery() as IQueryable<TEntity>
                ?? Enumerable.Empty<TEntity>().AsQueryable();
        }

        protected IDbContextProvider.IDataBaseContext GetDbContext() => _dbContext;

        public override async Task<List<TEntity>> GetListAsync(CancellationToken cancellationToken = default)
        {
            return await GetQueryable().ToListAsync(cancellationToken);
        }

        public override async Task<List<TEntity>> GetListAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
        {
            return await GetQueryable().Where(predicate).ToListAsync(cancellationToken);
        }

        public override async Task<List<TEntity>> GetListAsync(Expression<Func<TEntity, bool>> predicate, Expression<Func<TEntity, object>> orderBy, bool ascending = true, CancellationToken cancellationToken = default)
        {
            var query = GetQueryable().Where(predicate);
            query = ascending ? query.OrderBy(orderBy) : query.OrderByDescending(orderBy);
            return await query.ToListAsync(cancellationToken);
        }

        public override async Task<TEntity?> GetAsync(TKey id, CancellationToken cancellationToken = default)
        {
            return await GetQueryable().Where(e => e.Id.Equals(id)).FirstOrDefaultAsync(cancellationToken);
        }

        public override async Task<TEntity?> GetAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
        {
            return await GetQueryable().Where(predicate).FirstOrDefaultAsync(cancellationToken);
        }

        public override async Task<TEntity> InsertAsync(TEntity entity, CancellationToken cancellationToken = default)
        {
            await _dbContext.Set<TEntity>().AddAsync(entity, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return entity;
        }

        public override async Task<IEnumerable<TEntity>> InsertRangeAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
        {
            await _dbContext.Set<TEntity>().AddRangeAsync(entities, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return entities;
        }

        public override async Task<TEntity> UpdateAsync(TEntity entity, CancellationToken cancellationToken = default)
        {
            _dbContext.Set<TEntity>().Update(entity);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return entity;
        }

        public override async Task<IEnumerable<TEntity>> UpdateRangeAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
        {
            _dbContext.Set<TEntity>().UpdateRange(entities);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return entities;
        }

        public override async Task DeleteAsync(TEntity entity, CancellationToken cancellationToken = default)
        {
            _dbContext.Set<TEntity>().Remove(entity);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        public override async Task DeleteAsync(TKey id, CancellationToken cancellationToken = default)
        {
            var entity = await GetAsync(id, cancellationToken);
            if (entity != null)
            {
                _dbContext.Set<TEntity>().Remove(entity);
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
        }

        public override async Task DeleteRangeAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
        {
            _dbContext.Set<TEntity>().RemoveRange(entities);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        public override async Task DeleteRangeAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
        {
            var entities = await GetQueryable().Where(predicate).ToListAsync(cancellationToken);
            _dbContext.Set<TEntity>().RemoveRange(entities);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        public override async Task<PagedResult<TEntity>> GetPagedAsync(int pageIndex, int pageSize, CancellationToken cancellationToken = default)
        {
            var query = GetQueryable();
            var totalCount = await query.LongCountAsync(cancellationToken);
            var items = await query.Skip(pageIndex * pageSize).Take(pageSize).ToListAsync(cancellationToken);
            return new PagedResult<TEntity>(items, (int)totalCount, pageIndex, pageSize);
        }

        public override async Task<PagedResult<TEntity>> GetPagedAsync(int pageIndex, int pageSize, Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
        {
            var query = GetQueryable().Where(predicate);
            var totalCount = await query.LongCountAsync(cancellationToken);
            var items = await query.Skip(pageIndex * pageSize).Take(pageSize).ToListAsync(cancellationToken);
            return new PagedResult<TEntity>(items, (int)totalCount, pageIndex, pageSize);
        }

        public override async Task<PagedResult<TEntity>> GetPagedAsync(int pageIndex, int pageSize, Expression<Func<TEntity, bool>> predicate, Expression<Func<TEntity, object>> orderBy, bool ascending = true, CancellationToken cancellationToken = default)
        {
            var query = GetQueryable().Where(predicate);
            query = ascending ? query.OrderBy(orderBy) : query.OrderByDescending(orderBy);
            var totalCount = await query.LongCountAsync(cancellationToken);
            var items = await query.Skip(pageIndex * pageSize).Take(pageSize).ToListAsync(cancellationToken);
            return new PagedResult<TEntity>(items, (int)totalCount, pageIndex, pageSize);
        }

        public override async Task<PagedResult<TEntity>> GetPagedAsync(int pageIndex, int pageSize, Expression<Func<TEntity, object>> orderBy, bool ascending = true, CancellationToken cancellationToken = default)
        {
            var query = GetQueryable();
            query = ascending ? query.OrderBy(orderBy) : query.OrderByDescending(orderBy);
            var totalCount = await query.LongCountAsync(cancellationToken);
            var items = await query.Skip(pageIndex * pageSize).Take(pageSize).ToListAsync(cancellationToken);
            return new PagedResult<TEntity>(items, (int)totalCount, pageIndex, pageSize);
        }

        public override async Task<long> GetCountAsync(CancellationToken cancellationToken = default)
        {
            return await GetQueryable().LongCountAsync(cancellationToken);
        }

        public override async Task<long> GetCountAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
        {
            return await GetQueryable().Where(predicate).LongCountAsync(cancellationToken);
        }

        public override async Task<bool> AnyAsync(CancellationToken cancellationToken = default)
        {
            return await GetQueryable().AnyAsync(cancellationToken);
        }

        public override async Task<bool> AnyAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
        {
            return await GetQueryable().AnyAsync(predicate, cancellationToken);
        }

        public override async Task<bool> ExistsAsync(TKey id, CancellationToken cancellationToken = default)
        {
            return await GetQueryable().AnyAsync(e => e.Id.Equals(id), cancellationToken);
        }
    }
}
