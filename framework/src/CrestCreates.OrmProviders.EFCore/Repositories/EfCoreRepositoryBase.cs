using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Domain.DataFilter;
using CrestCreates.Domain.Entities;
using CrestCreates.Domain.Exceptions;
using CrestCreates.Domain.Repositories;
using CrestCreates.Domain.Shared.DTOs;
using CrestCreates.Domain.Shared.Entities;
using CrestCreates.Domain.Shared.Entities.Auditing;
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
            return GetNativeQueryable(_dbContext.Queryable<TEntity>().IgnoreQueryFilters().GetNativeQuery());
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
            if (entity is IHasConcurrencyStamp stampEntity)
            {
                var dbContext = (DbContext)_dbContext.GetNativeContext();
                var oldStamp = stampEntity.ConcurrencyStamp;
                stampEntity.ConcurrencyStamp = Guid.NewGuid().ToString("N");
                var entry = dbContext.Set<TEntity>().Update(entity);
                entry.Property(nameof(IHasConcurrencyStamp.ConcurrencyStamp)).OriginalValue = oldStamp;
            }
            else
            {
                _dbContext.Set<TEntity>().Update(entity);
            }

            try
            {
                await SaveChangesIfNoActiveTransactionAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                throw new CrestConcurrencyException(typeof(TEntity).Name, entity.Id);
            }
            return entity;
        }

        public override async Task<IEnumerable<TEntity>> UpdateRangeAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
        {
            var entityList = entities.ToList();
            if (entityList.Any(e => e is IHasConcurrencyStamp))
                throw new NotSupportedException("UpdateRangeAsync does not support entities with concurrency stamps. Use UpdateAsync for concurrency-safe updates.");
            _dbContext.Set<TEntity>().UpdateRange(entityList);
            await SaveChangesIfNoActiveTransactionAsync(cancellationToken);
            return entityList;
        }

        public override async Task DeleteAsync(TEntity entity, CancellationToken cancellationToken = default)
        {
            if (entity is IHasConcurrencyStamp)
            {
                var dbContext = (DbContext)_dbContext.GetNativeContext();
                var entry = dbContext.Entry(entity);
                entry.Property(nameof(IHasConcurrencyStamp.ConcurrencyStamp)).OriginalValue = ((IHasConcurrencyStamp)entity).ConcurrencyStamp;
                entry.State = EntityState.Deleted;
            }
            else
            {
                _dbContext.Set<TEntity>().Remove(entity);
            }

            try
            {
                await SaveChangesIfNoActiveTransactionAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                throw new CrestConcurrencyException(typeof(TEntity).Name, entity.Id);
            }
        }

        public override async Task DeleteAsync(TKey id, CancellationToken cancellationToken = default)
        {
            var entity = await GetAsync(id, cancellationToken);
            if (entity != null)
            {
                _dbContext.Set<TEntity>().Remove(entity);
                await SaveChangesIfNoActiveTransactionAsync(cancellationToken);
            }
        }

        public override async Task DeleteAsync(TKey id, string expectedStamp, CancellationToken cancellationToken = default)
        {
            var dbContext = (DbContext)_dbContext.GetNativeContext();
            var rows = await dbContext.Set<TEntity>()
                .Where(e => e.Id.Equals(id) && EF.Property<string>(e, "ConcurrencyStamp") == expectedStamp)
                .ExecuteDeleteAsync(cancellationToken);
            if (rows == 0) throw new CrestConcurrencyException(typeof(TEntity).Name, id);
        }

        public override async Task DeleteRangeAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
        {
            _dbContext.Set<TEntity>().RemoveRange(entities);
            await SaveChangesIfNoActiveTransactionAsync(cancellationToken);
        }

        public override async Task DeleteRangeAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
        {
            var entities = await GetQueryable().Where(predicate).ToListAsync(cancellationToken);
            _dbContext.Set<TEntity>().RemoveRange(entities);
            await SaveChangesIfNoActiveTransactionAsync(cancellationToken);
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
