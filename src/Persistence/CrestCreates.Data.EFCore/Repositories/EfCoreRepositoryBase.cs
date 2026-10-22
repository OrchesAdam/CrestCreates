using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Data.Abstractions;
using CrestCreates.Domain.DataFilter;
using CrestCreates.Domain.Entities;
using CrestCreates.Domain.Exceptions;
using CrestCreates.Domain.Repositories;
using CrestCreates.Domain.Shared.DTOs;
using CrestCreates.Domain.Shared.Entities;
using CrestCreates.Domain.Shared.Entities.Auditing;
using CrestCreates.DataFilter.Entities;
using CrestCreates.MultiTenancy.Abstract;
using DataFilterMultiTenant = CrestCreates.DataFilter.Entities.IMultiTenant;
using EfMultiTenant = CrestCreates.Data.EFCore.MultiTenancy.IMultiTenant;
using Microsoft.EntityFrameworkCore;
using IDbContextProvider = CrestCreates.DbContextProvider.Abstract;

namespace CrestCreates.Data.EFCore.Repositories
{
    /// <summary>
    /// 平台仓储基类：数据动作把「传入 CT」与「受管执行 token」组合为有效 token
    /// （操作层 token 组合，见设计 §4.1 R2-S137-03）。
    /// </summary>
    public abstract class EfCoreRepositoryBase<TEntity, TKey> : CrestRepositoryBase<TEntity, TKey>
        where TEntity : class, IEntity<TKey>
        where TKey : IEquatable<TKey>
    {
        protected readonly IDbContextProvider.IDataBaseContext _dbContext;
        private readonly UnitOfWorkChainNode? _chainNode;

        public EfCoreRepositoryBase(
            IDbContextProvider.IDataBaseContext dbContext,
            ICurrentTenant currentTenant,
            DataFilterState dataFilterState,
            UnitOfWorkChainNode? chainNode = null)
        {
            _dbContext = dbContext;
            CurrentTenant = currentTenant;
            DataFilterState = dataFilterState;
            _chainNode = chainNode;
        }

        public override IQueryable<TEntity> GetQueryableUnfiltered()
        {
            return GetNativeQueryable(_dbContext.Queryable<TEntity>().IgnoreQueryFilters().GetNativeQuery());
        }

        protected IDbContextProvider.IDataBaseContext GetDbContext() => _dbContext;

        public override async Task<List<TEntity>> GetListAsync(CancellationToken cancellationToken = default)
        {
            using var linked = UnitOfWorkExecutionContext.CombineWithCurrent(cancellationToken, out var effective);
            return await GetQueryable().ToListAsync(effective);
        }

        public override async Task<List<TEntity>> GetListAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
        {
            using var linked = UnitOfWorkExecutionContext.CombineWithCurrent(cancellationToken, out var effective);
            return await GetQueryable().Where(predicate).ToListAsync(effective);
        }

        public override async Task<List<TEntity>> GetListAsync(Expression<Func<TEntity, bool>> predicate, Expression<Func<TEntity, object>> orderBy, bool ascending = true, CancellationToken cancellationToken = default)
        {
            using var linked = UnitOfWorkExecutionContext.CombineWithCurrent(cancellationToken, out var effective);
            var query = GetQueryable().Where(predicate);
            query = ascending ? query.OrderBy(orderBy) : query.OrderByDescending(orderBy);
            return await query.ToListAsync(effective);
        }

        public override async Task<TEntity?> GetAsync(TKey id, CancellationToken cancellationToken = default)
        {
            using var linked = UnitOfWorkExecutionContext.CombineWithCurrent(cancellationToken, out var effective);
            return await GetQueryable().Where(e => e.Id.Equals(id)).FirstOrDefaultAsync(effective);
        }

        public override async Task<TEntity?> GetAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
        {
            using var linked = UnitOfWorkExecutionContext.CombineWithCurrent(cancellationToken, out var effective);
            return await GetQueryable().Where(predicate).FirstOrDefaultAsync(effective);
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
            await EnsurePersistedTenantOwnershipAsync(entity, cancellationToken);

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
            await EnsurePersistedTenantOwnershipAsync(entity, cancellationToken);

            if (entity is ISoftDelete { IsDeleted: true })
            {
                return;
            }

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
            if (!typeof(IHasConcurrencyStamp).IsAssignableFrom(typeof(TEntity)))
            {
                await DeleteAsync(id, cancellationToken);
                return;
            }

            await DeleteStampedTrackedAsync(id, expectedStamp, cancellationToken);
        }

        public override async Task DeleteRangeAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
        {
            ThrowIfBatchDeleteHasNoPerRowStamp();

            var liveEntities = entities
                .Where(entity => entity is not ISoftDelete { IsDeleted: true })
                .ToList();
            _dbContext.Set<TEntity>().RemoveRange(liveEntities);
            await SaveChangesIfNoActiveTransactionAsync(cancellationToken);
        }

        public override async Task DeleteRangeAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
        {
            ThrowIfBatchDeleteHasNoPerRowStamp();

            using var linked = UnitOfWorkExecutionContext.CombineWithCurrent(cancellationToken, out var effective);
            var entities = await GetQueryable().Where(predicate).ToListAsync(effective);
            _dbContext.Set<TEntity>().RemoveRange(entities);
            await SaveChangesIfNoActiveTransactionAsync(effective);
        }

        public override async Task<PagedResult<TEntity>> GetPagedAsync(int pageIndex, int pageSize, CancellationToken cancellationToken = default)
        {
            using var linked = UnitOfWorkExecutionContext.CombineWithCurrent(cancellationToken, out var effective);
            var query = GetQueryable();
            var totalCount = await query.LongCountAsync(effective);
            var items = await query.Skip(pageIndex * pageSize).Take(pageSize).ToListAsync(effective);
            return new PagedResult<TEntity>(items, (int)totalCount, pageIndex, pageSize);
        }

        public override async Task<PagedResult<TEntity>> GetPagedAsync(int pageIndex, int pageSize, Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
        {
            using var linked = UnitOfWorkExecutionContext.CombineWithCurrent(cancellationToken, out var effective);
            var query = GetQueryable().Where(predicate);
            var totalCount = await query.LongCountAsync(effective);
            var items = await query.Skip(pageIndex * pageSize).Take(pageSize).ToListAsync(effective);
            return new PagedResult<TEntity>(items, (int)totalCount, pageIndex, pageSize);
        }

        public override async Task<PagedResult<TEntity>> GetPagedAsync(int pageIndex, int pageSize, Expression<Func<TEntity, bool>> predicate, Expression<Func<TEntity, object>> orderBy, bool ascending = true, CancellationToken cancellationToken = default)
        {
            using var linked = UnitOfWorkExecutionContext.CombineWithCurrent(cancellationToken, out var effective);
            var query = GetQueryable().Where(predicate);
            query = ascending ? query.OrderBy(orderBy) : query.OrderByDescending(orderBy);
            var totalCount = await query.LongCountAsync(effective);
            var items = await query.Skip(pageIndex * pageSize).Take(pageSize).ToListAsync(effective);
            return new PagedResult<TEntity>(items, (int)totalCount, pageIndex, pageSize);
        }

        public override async Task<PagedResult<TEntity>> GetPagedAsync(int pageIndex, int pageSize, Expression<Func<TEntity, object>> orderBy, bool ascending = true, CancellationToken cancellationToken = default)
        {
            using var linked = UnitOfWorkExecutionContext.CombineWithCurrent(cancellationToken, out var effective);
            var query = GetQueryable();
            query = ascending ? query.OrderBy(orderBy) : query.OrderByDescending(orderBy);
            var totalCount = await query.LongCountAsync(effective);
            var items = await query.Skip(pageIndex * pageSize).Take(pageSize).ToListAsync(effective);
            return new PagedResult<TEntity>(items, (int)totalCount, pageIndex, pageSize);
        }

        public override async Task<long> GetCountAsync(CancellationToken cancellationToken = default)
        {
            using var linked = UnitOfWorkExecutionContext.CombineWithCurrent(cancellationToken, out var effective);
            return await GetQueryable().LongCountAsync(effective);
        }

        public override async Task<long> GetCountAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
        {
            using var linked = UnitOfWorkExecutionContext.CombineWithCurrent(cancellationToken, out var effective);
            return await GetQueryable().Where(predicate).LongCountAsync(effective);
        }

        public override async Task<bool> AnyAsync(CancellationToken cancellationToken = default)
        {
            using var linked = UnitOfWorkExecutionContext.CombineWithCurrent(cancellationToken, out var effective);
            return await GetQueryable().AnyAsync(effective);
        }

        public override async Task<bool> AnyAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
        {
            using var linked = UnitOfWorkExecutionContext.CombineWithCurrent(cancellationToken, out var effective);
            return await GetQueryable().AnyAsync(predicate, effective);
        }

        public override async Task<bool> ExistsAsync(TKey id, CancellationToken cancellationToken = default)
        {
            using var linked = UnitOfWorkExecutionContext.CombineWithCurrent(cancellationToken, out var effective);
            return await GetQueryable().AnyAsync(e => e.Id.Equals(id), effective);
        }

        private async Task DeleteStampedTrackedAsync(TKey id, string expectedStamp, CancellationToken cancellationToken)
        {
            using var linked = UnitOfWorkExecutionContext.CombineWithCurrent(cancellationToken, _chainNode, out var effective);

            var entity = await GetQueryable()
                .Where(e => e.Id.Equals(id))
                .FirstOrDefaultAsync(effective);
            if (entity is null)
            {
                // Zero matching rows (including rows hidden by filters) stays a concurrency failure
                // for stamped deletes so callers cannot probe other tenants' existence.
                throw new CrestConcurrencyException(typeof(TEntity).Name, id);
            }

            if (entity is not IHasConcurrencyStamp stampEntity)
            {
                _dbContext.Set<TEntity>().Remove(entity);
            }
            else
            {
                var dbContext = (DbContext)_dbContext.GetNativeContext();
                var entry = dbContext.Entry(entity);
                entry.Property(nameof(IHasConcurrencyStamp.ConcurrencyStamp)).OriginalValue = expectedStamp;
                stampEntity.ConcurrencyStamp = Guid.NewGuid().ToString("N");
                entry.State = EntityState.Deleted;
            }

            try
            {
                await SaveChangesIfNoActiveTransactionAsync(effective);
            }
            catch (DbUpdateConcurrencyException)
            {
                throw new CrestConcurrencyException(typeof(TEntity).Name, id);
            }
        }

        private static void ThrowIfBatchDeleteHasNoPerRowStamp()
        {
            if (typeof(IHasConcurrencyStamp).IsAssignableFrom(typeof(TEntity)))
            {
                throw new NotSupportedException(
                    "DeleteRangeAsync does not accept entities with concurrency stamps because the batch overload has no per-row expectedStamp. Delete stamped entities one by one with DeleteAsync(id, expectedStamp).");
            }
        }


        private static readonly bool HasTenantMarker =
            typeof(IMustHaveTenant).IsAssignableFrom(typeof(TEntity)) ||
            typeof(DataFilterMultiTenant).IsAssignableFrom(typeof(TEntity)) ||
            typeof(EfMultiTenant).IsAssignableFrom(typeof(TEntity));

        private static string? ReadTenantId(TEntity entity)
        {
            return entity switch
            {
                IMustHaveTenant mustHaveTenant => mustHaveTenant.TenantId,
                DataFilterMultiTenant dataFilterTenant => dataFilterTenant.TenantId,
                EfMultiTenant efTenant => efTenant.TenantId,
                _ => null
            };
        }

        private async Task EnsurePersistedTenantOwnershipAsync(TEntity entity, CancellationToken cancellationToken)
        {
            if (!HasTenantMarker)
            {
                return;
            }

            var persisted = await GetQueryableUnfiltered()
                .Where(e => e.Id.Equals(entity.Id))
                .FirstOrDefaultAsync(cancellationToken);
            var currentTenant = CurrentTenant?.Id;
            var persistedTenant = persisted is null ? null : ReadTenantId(persisted);
            var submittedTenant = ReadTenantId(entity);

            if (persisted is null)
            {
                throw new InvalidOperationException(
                    $"{typeof(TEntity).Name} (Id={entity.Id}) does not exist or is not visible for a tenant-scoped write.");
            }

            if (!string.Equals(persistedTenant, currentTenant, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"{typeof(TEntity).Name} (Id={entity.Id}) belongs to tenant '{persistedTenant}', not the current tenant '{currentTenant}'.");
            }

            if (!string.Equals(submittedTenant, currentTenant, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Cannot write {typeof(TEntity).Name} for tenant '{submittedTenant}' while the current tenant is '{currentTenant}'.");
            }
        }

        private async Task SaveChangesIfNoActiveTransactionAsync(CancellationToken cancellationToken)
        {
            if (_dbContext.CurrentTransaction == null)
            {
                using var linked = UnitOfWorkExecutionContext.CombineWithCurrent(cancellationToken, _chainNode, out var effective);
                await _dbContext.SaveChangesAsync(effective);
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
