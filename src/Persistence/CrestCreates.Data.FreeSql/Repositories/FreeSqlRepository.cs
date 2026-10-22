using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using FreeSql;
using CrestCreates.Data.Abstractions;
using CrestCreates.DataFilter.Entities;
using CrestCreates.MultiTenancy.Abstract;
using CrestCreates.Domain.Entities;
using CrestCreates.Domain.Repositories;
using CrestCreates.Domain.Exceptions;
using CrestCreates.Domain.Shared.DTOs;
using CrestCreates.Domain.Shared.Entities;
using CrestCreates.Domain.Shared.Entities.Auditing;
using Microsoft.Extensions.Logging;

namespace CrestCreates.Data.FreeSql.Repositories
{
    /// <summary>
    /// FreeSql 仓储基类（支持 UnitOfWorkManager）
    /// </summary>
    /// <typeparam name="TEntity">实体类型</typeparam>
    /// <typeparam name="TKey">主键类型</typeparam>
    /// <remarks>
    /// 官方文档: https://freesql.net/guide/unitofwork-manager.html#扩展-重写仓储
    /// 此实现继承自 CrestRepositoryBase，并绑定到 UnitOfWorkManager
    /// </remarks>
    public abstract class FreeSqlRepository<TEntity, TKey> : CrestRepositoryBase<TEntity, TKey>
        where TEntity : class, IEntity<TKey>
        where TKey : IEquatable<TKey>
    {
        protected readonly IFreeSql _orm;
        protected readonly ILogger<FreeSqlRepository<TEntity, TKey>>? _logger;
        private readonly ICurrentUserProvider? _currentUserProvider;
        private readonly ICurrentTenant? _currentTenant;
        private static readonly bool HasTenantMarker =
            typeof(IMustHaveTenant).IsAssignableFrom(typeof(TEntity)) ||
            typeof(IMultiTenant).IsAssignableFrom(typeof(TEntity));
        private static readonly bool HasRequiredTenantMarker =
            typeof(IMustHaveTenant).IsAssignableFrom(typeof(TEntity));

        /// <summary>
        /// 构造函数（支持 UnitOfWorkManager）
        /// </summary>
        /// <param name="uowManager">工作单元管理器</param>
        /// <param name="logger">日志记录器</param>
        public FreeSqlRepository(
            UnitOfWork.FreeSqlUnitOfWorkManager uowManager,
            ILogger<FreeSqlRepository<TEntity, TKey>>? logger = null,
            ICurrentUserProvider? currentUserProvider = null,
            ICurrentTenant? currentTenant = null)
        {
            _orm = uowManager.Orm ?? throw new InvalidOperationException("UnitOfWorkManager.Orm must not be null.");
            _logger = logger;
            _currentUserProvider = currentUserProvider;
            _currentTenant = currentTenant;

            if (typeof(ISoftDelete).IsAssignableFrom(typeof(TEntity)))
            {
                _orm.GlobalFilter.Apply<ISoftDelete>("SoftDelete", entity => entity.IsDeleted == false);
            }
        }

        private static Expression<Func<TEntity, bool>> InterfacePropertyEquals(
            Type interfaceType,
            string propertyName,
            object value)
        {
            var parameter = Expression.Parameter(typeof(TEntity), "entity");
            var property = Expression.Property(Expression.Convert(parameter, interfaceType), propertyName);
            var body = Expression.Equal(property, Expression.Constant(value, property.Type));
            return Expression.Lambda<Func<TEntity, bool>>(body, parameter);
        }

        private static Expression<Func<TEntity, bool>> LiveRowPredicate()
        {
            return InterfacePropertyEquals(typeof(ISoftDelete), nameof(ISoftDelete.IsDeleted), false);
        }

        private static Expression<Func<TEntity, bool>> ConcurrencyStampPredicate(string stamp)
        {
            return InterfacePropertyEquals(
                typeof(IHasConcurrencyStamp),
                nameof(IHasConcurrencyStamp.ConcurrencyStamp),
                stamp);
        }


        private static Expression<Func<TEntity, bool>> OptionalTenantPredicate(string? tenantId)
        {
            var parameter = Expression.Parameter(typeof(TEntity), "entity");
            var property = Expression.Property(
                Expression.Convert(parameter, typeof(IMultiTenant)),
                nameof(IMultiTenant.TenantId));
            Expression body = Expression.Equal(property, Expression.Constant(null, property.Type));
            if (!string.IsNullOrEmpty(tenantId))
            {
                body = Expression.OrElse(
                    Expression.Equal(property, Expression.Constant(tenantId, property.Type)),
                    body);
            }

            return Expression.Lambda<Func<TEntity, bool>>(body, parameter);
        }

        private Expression<Func<TEntity, bool>> TenantPredicateForCurrentTenant()
        {
            if (!HasTenantMarker)
            {
                throw new InvalidOperationException("The entity has no tenant marker.");
            }

            var tenantId = _currentTenant?.Id;
            if (HasRequiredTenantMarker)
            {
                if (string.IsNullOrEmpty(tenantId))
                {
                    throw new InvalidOperationException(
                        $"{typeof(TEntity).Name} requires a current tenant for this operation.");
                }

                return InterfacePropertyEquals(
                    typeof(IMustHaveTenant),
                    nameof(IMustHaveTenant.TenantId),
                    tenantId);
            }

            return OptionalTenantPredicate(tenantId);
        }

        private ISelect<TEntity> SelectForCurrentTenant()
        {
            var query = _orm.Select<TEntity>();
            return HasTenantMarker ? query.Where(TenantPredicateForCurrentTenant()) : query;
        }

        private void ApplyTenantOnInsert(TEntity entity)
        {
            if (!HasTenantMarker)
            {
                return;
            }

            var tenantId = _currentTenant?.Id;
            if (HasRequiredTenantMarker)
            {
                if (string.IsNullOrEmpty(tenantId))
                {
                    throw new InvalidOperationException(
                        $"{typeof(TEntity).Name} requires a current tenant for insert.");
                }

                var mustHaveTenant = (IMustHaveTenant)entity;
                if (string.IsNullOrEmpty(mustHaveTenant.TenantId))
                {
                    mustHaveTenant.TenantId = tenantId;
                }
                else if (!string.Equals(mustHaveTenant.TenantId, tenantId, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Cannot insert {typeof(TEntity).Name} for tenant '{mustHaveTenant.TenantId}' while the current tenant is '{tenantId}'.");
                }
            }
            else
            {
                var multiTenant = (IMultiTenant)entity;
                if (string.IsNullOrEmpty(multiTenant.TenantId) && !string.IsNullOrEmpty(tenantId))
                {
                    multiTenant.TenantId = tenantId;
                }
                else if (!string.IsNullOrEmpty(multiTenant.TenantId) &&
                         !string.Equals(multiTenant.TenantId, tenantId, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Cannot insert {typeof(TEntity).Name} for tenant '{multiTenant.TenantId}' while the current tenant is '{tenantId}'.");
                }
            }
        }

        private static string? ReadTenantId(TEntity entity)
        {
            return entity switch
            {
                IMustHaveTenant mustHaveTenant => mustHaveTenant.TenantId,
                IMultiTenant multiTenant => multiTenant.TenantId,
                _ => null
            };
        }

        private async Task<TEntity?> FindPersistedForTenantCheckAsync(
            TKey id,
            CancellationToken cancellationToken)
        {
            if (!HasTenantMarker)
            {
                return null;
            }

            return await _orm.Select<TEntity>()
                .Where(e => e.Id.Equals(id))
                .DisableGlobalFilter("MultiTenant", "MustHaveTenant")
                .FirstAsync(cancellationToken);
        }

        private void EnsurePersistedTenant(TEntity? persisted, TKey id)
        {
            if (!HasTenantMarker)
            {
                return;
            }

            if (persisted is null)
            {
                return;
            }

            var currentTenant = _currentTenant?.Id;
            var persistedTenant = ReadTenantId(persisted);
            if (HasRequiredTenantMarker)
            {
                if (!string.Equals(persistedTenant, currentTenant, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"{typeof(TEntity).Name} (Id={id}) belongs to tenant '{persistedTenant}', not the current tenant '{currentTenant}'.");
                }
            }
            else if (!string.IsNullOrEmpty(persistedTenant) &&
                     !string.Equals(persistedTenant, currentTenant, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"{typeof(TEntity).Name} (Id={id}) belongs to tenant '{persistedTenant}', not the current tenant '{currentTenant}'.");
            }
        }


        private IUpdate<TEntity> WithTenant(IUpdate<TEntity> update)
        {
            return HasTenantMarker ? update.Where(TenantPredicateForCurrentTenant()) : update;
        }

        private IDelete<TEntity> WithTenant(IDelete<TEntity> delete)
        {
            return HasTenantMarker ? delete.Where(TenantPredicateForCurrentTenant()) : delete;
        }

        private void EnsureSubmittedTenant(TEntity entity, TEntity? persisted)
        {
            if (!HasTenantMarker)
            {
                return;
            }

            var currentTenant = _currentTenant?.Id;
            var submittedTenant = ReadTenantId(entity);
            if (HasRequiredTenantMarker)
            {
                if (!string.Equals(submittedTenant, currentTenant, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Cannot write {typeof(TEntity).Name} for tenant '{submittedTenant}' while the current tenant is '{currentTenant}'.");
                }
                return;
            }

            if (!string.IsNullOrEmpty(submittedTenant) &&
                !string.Equals(submittedTenant, currentTenant, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Cannot write {typeof(TEntity).Name} for tenant '{submittedTenant}' while the current tenant is '{currentTenant}'.");
            }

            var persistedTenant = persisted is null ? null : ReadTenantId(persisted);
            if (string.IsNullOrEmpty(submittedTenant) && !string.IsNullOrEmpty(persistedTenant))
            {
                throw new InvalidOperationException(
                    $"Cannot clear the tenant of {typeof(TEntity).Name} (Id={entity.Id}).");
            }
        }

        public override IQueryable<TEntity> GetQueryable()
        {
            throw new NotSupportedException(
                "FreeSql repositories do not expose a composable IQueryable. Use the asynchronous repository query methods or the SDK query API directly.");
        }

        public override async Task<List<TEntity>> GetListAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                _logger?.LogDebug("Getting all entities {EntityType}", typeof(TEntity).Name);
                var result = await SelectForCurrentTenant().ToListAsync(cancellationToken);
                _logger?.LogDebug("Got {Count} entities {EntityType}", result.Count, typeof(TEntity).Name);
                return result;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error getting all entities {EntityType}", typeof(TEntity).Name);
                throw;
            }
        }

        public override async Task<List<TEntity>> GetListAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                _logger?.LogDebug("Finding entities {EntityType}", typeof(TEntity).Name);
                var result = await SelectForCurrentTenant().Where(predicate).ToListAsync(cancellationToken);
                _logger?.LogDebug("Found {Count} entities {EntityType}", result.Count, typeof(TEntity).Name);
                return result;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error finding entities {EntityType}", typeof(TEntity).Name);
                throw;
            }
        }

        public override async Task<List<TEntity>> GetListAsync(Expression<Func<TEntity, bool>> predicate, Expression<Func<TEntity, object>> orderBy, bool ascending = true, CancellationToken cancellationToken = default)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                _logger?.LogDebug("Finding entities {EntityType} with order", typeof(TEntity).Name);
                var query = SelectForCurrentTenant().Where(predicate);
                query = ascending ? query.OrderBy(orderBy) : query.OrderByDescending(orderBy);
                var result = await query.ToListAsync(cancellationToken);
                _logger?.LogDebug("Found {Count} entities {EntityType}", result.Count, typeof(TEntity).Name);
                return result;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error finding entities {EntityType}", typeof(TEntity).Name);
                throw;
            }
        }

        public override async Task<TEntity?> GetAsync(TKey id, CancellationToken cancellationToken = default)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                _logger?.LogDebug("Getting entity {EntityType} by id: {Id}", typeof(TEntity).Name, id);
                var result = await SelectForCurrentTenant().Where(e => e.Id.Equals(id)).FirstAsync(cancellationToken);
                _logger?.LogDebug("Got entity {EntityType} by id: {Id}", typeof(TEntity).Name, id);
                return result;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error getting entity {EntityType} by id: {Id}", typeof(TEntity).Name, id);
                throw;
            }
        }

        public override async Task<TEntity?> GetAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                _logger?.LogDebug("Getting entity {EntityType} by predicate", typeof(TEntity).Name);
                var result = await SelectForCurrentTenant().Where(predicate).FirstAsync(cancellationToken);
                _logger?.LogDebug("Got entity {EntityType}", typeof(TEntity).Name);
                return result;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error getting entity {EntityType} by predicate", typeof(TEntity).Name);
                throw;
            }
        }

        public override async Task<TEntity> InsertAsync(TEntity entity, CancellationToken cancellationToken = default)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                _logger?.LogDebug("Adding entity {EntityType}", typeof(TEntity).Name);
                ApplyTenantOnInsert(entity);
                await _orm.Insert(entity).ExecuteAffrowsAsync(cancellationToken);
                _logger?.LogDebug("Added entity {EntityType} with id: {Id}", typeof(TEntity).Name, entity.Id);
                return entity;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error adding entity {EntityType}", typeof(TEntity).Name);
                throw;
            }
        }

        public override async Task<IEnumerable<TEntity>> InsertRangeAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                _logger?.LogDebug("Inserting many entities {EntityType}", typeof(TEntity).Name);
                var entityList = entities.ToList();
                foreach (var item in entityList)
                {
                    ApplyTenantOnInsert(item);
                }
                await _orm.Insert(entityList).ExecuteAffrowsAsync(cancellationToken);
                _logger?.LogDebug("Inserted {Count} entities {EntityType}", entityList.Count, typeof(TEntity).Name);
                return entityList;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error inserting many entities {EntityType}", typeof(TEntity).Name);
                throw;
            }
        }

        public override async Task<TEntity> UpdateAsync(TEntity entity, CancellationToken cancellationToken = default)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                _logger?.LogDebug("Updating entity {EntityType} with id: {Id}", typeof(TEntity).Name, entity.Id);
                if (HasTenantMarker)
                {
                    var persistedForTenant = await FindPersistedForTenantCheckAsync(entity.Id, cancellationToken);
                    EnsurePersistedTenant(persistedForTenant, entity.Id);
                    EnsureSubmittedTenant(entity, persistedForTenant);
                }
                if (entity is IHasConcurrencyStamp stamp)
                {
                    var oldStamp = stamp.ConcurrencyStamp;
                    stamp.ConcurrencyStamp = Guid.NewGuid().ToString();
                    var update = _orm.Update<TEntity>()
                        .SetSource(entity)
                        .Where(e => e.Id.Equals(entity.Id))
                        .Where(ConcurrencyStampPredicate(oldStamp));
                    if (HasTenantMarker)
                    {
                        update = update.Where(TenantPredicateForCurrentTenant());
                    }
                    var rows = await update.ExecuteAffrowsAsync(cancellationToken);
                    if (rows == 0) throw new CrestConcurrencyException(typeof(TEntity).Name, entity.Id);
                }
                else
                {
                    var update = _orm.Update<TEntity>().SetSource(entity);
                    if (HasTenantMarker)
                    {
                        update = update.Where(e => e.Id.Equals(entity.Id))
                            .Where(TenantPredicateForCurrentTenant());
                    }
                    await update.ExecuteAffrowsAsync(cancellationToken);
                }
                _logger?.LogDebug("Updated entity {EntityType} with id: {Id}", typeof(TEntity).Name, entity.Id);
                return entity;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error updating entity {EntityType} with id: {Id}", typeof(TEntity).Name, entity.Id);
                throw;
            }
        }

        public override async Task<IEnumerable<TEntity>> UpdateRangeAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                _logger?.LogDebug("Updating many entities {EntityType}", typeof(TEntity).Name);
                var entityList = entities.ToList();
                if (HasTenantMarker)
                {
                    throw new NotSupportedException(
                        "UpdateRangeAsync does not validate persisted tenant ownership. Update tenant-scoped entities one by one with UpdateAsync.");
                }
                if (entityList.Any(e => e is IHasConcurrencyStamp))
                    throw new NotSupportedException("UpdateRangeAsync does not support entities with IHasConcurrencyStamp. Use UpdateAsync for concurrency-safe updates.");
                await _orm.Update<TEntity>().SetSource(entityList).ExecuteAffrowsAsync(cancellationToken);
                _logger?.LogDebug("Updated {Count} entities {EntityType}", entityList.Count, typeof(TEntity).Name);
                return entityList;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error updating many entities {EntityType}", typeof(TEntity).Name);
                throw;
            }
        }

        public override async Task DeleteAsync(TEntity entity, CancellationToken cancellationToken = default)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                _logger?.LogDebug("Deleting entity {EntityType} with id: {Id}", typeof(TEntity).Name, entity.Id);
                if (HasTenantMarker)
                {
                    var persistedForTenant = await FindPersistedForTenantCheckAsync(entity.Id, cancellationToken);
                    EnsurePersistedTenant(persistedForTenant, entity.Id);
                }
                if (entity is ISoftDelete)
                {
                    if (entity is IHasConcurrencyStamp stamp)
                    {
                        var rows = await FreeSqlSoftDeleteOperation.ExecuteAsync(
                            WithTenant(_orm.Update<TEntity>()
                                .Where(e => e.Id.Equals(entity.Id))
                                .Where(ConcurrencyStampPredicate(stamp.ConcurrencyStamp))
                                .Where(LiveRowPredicate())),
                            _currentUserProvider,
                            advanceConcurrencyStamp: true,
                            cancellationToken);
                        if (rows == 0) throw new CrestConcurrencyException(typeof(TEntity).Name, entity.Id);
                    }
                    else
                    {
                        await FreeSqlSoftDeleteOperation.ExecuteAsync(
                            WithTenant(_orm.Update<TEntity>()
                                .Where(e => e.Id.Equals(entity.Id))
                                .Where(LiveRowPredicate())),
                            _currentUserProvider,
                            advanceConcurrencyStamp: false,
                            cancellationToken);
                    }
                }
                else if (entity is IHasConcurrencyStamp stamp)
                {
                    var physicalDelete = _orm.Delete<TEntity>()
                        .Where(e => e.Id.Equals(entity.Id))
                        .Where(ConcurrencyStampPredicate(stamp.ConcurrencyStamp));
                    var rows = await WithTenant(physicalDelete).ExecuteAffrowsAsync(cancellationToken);
                    if (rows == 0) throw new CrestConcurrencyException(typeof(TEntity).Name, entity.Id);
                }
                else
                {
                    await _orm.Delete<TEntity>().Where(e => e.Id.Equals(entity.Id)).ExecuteAffrowsAsync(cancellationToken);
                }
                _logger?.LogDebug("Deleted entity {EntityType} with id: {Id}", typeof(TEntity).Name, entity.Id);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error deleting entity {EntityType} with id: {Id}", typeof(TEntity).Name, entity.Id);
                throw;
            }
        }

        public override async Task DeleteAsync(TKey id, CancellationToken cancellationToken = default)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                _logger?.LogDebug("Deleting entity {EntityType} by id: {Id}", typeof(TEntity).Name, id);
                if (HasTenantMarker)
                {
                    var persistedForTenant = await FindPersistedForTenantCheckAsync(id, cancellationToken);
                    EnsurePersistedTenant(persistedForTenant, id);
                    if (persistedForTenant is null)
                    {
                        return;
                    }
                }
                if (typeof(ISoftDelete).IsAssignableFrom(typeof(TEntity)))
                {
                    await FreeSqlSoftDeleteOperation.ExecuteAsync(
                        WithTenant(_orm.Update<TEntity>()
                            .Where(e => e.Id.Equals(id))
                            .Where(LiveRowPredicate())),
                        _currentUserProvider,
                        advanceConcurrencyStamp: typeof(IHasConcurrencyStamp).IsAssignableFrom(typeof(TEntity)),
                        cancellationToken);
                }
                else
                {
                    await WithTenant(_orm.Delete<TEntity>().Where(e => e.Id.Equals(id))).ExecuteAffrowsAsync(cancellationToken);
                }
                _logger?.LogDebug("Deleted entity {EntityType} by id: {Id}", typeof(TEntity).Name, id);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error deleting entity {EntityType} by id: {Id}", typeof(TEntity).Name, id);
                throw;
            }
        }

        public override async Task DeleteAsync(TKey id, string expectedStamp, CancellationToken cancellationToken = default)
        {
            try
            {
                if (!typeof(IHasConcurrencyStamp).IsAssignableFrom(typeof(TEntity)))
                {
                    await DeleteAsync(id, cancellationToken);
                    return;
                }
                _logger?.LogDebug("Deleting entity {EntityType} by id with concurrency check: {Id}", typeof(TEntity).Name, id);
                if (HasTenantMarker)
                {
                    var persistedForTenant = await FindPersistedForTenantCheckAsync(id, cancellationToken);
                    EnsurePersistedTenant(persistedForTenant, id);
                    if (persistedForTenant is null)
                    {
                        throw new CrestConcurrencyException(typeof(TEntity).Name, id);
                    }
                }
                if (typeof(ISoftDelete).IsAssignableFrom(typeof(TEntity)))
                {
                    var softRows = await FreeSqlSoftDeleteOperation.ExecuteAsync(
                        WithTenant(_orm.Update<TEntity>()
                            .Where(e => e.Id.Equals(id))
                            .Where(ConcurrencyStampPredicate(expectedStamp))
                            .Where(LiveRowPredicate())),
                        _currentUserProvider,
                        advanceConcurrencyStamp: true,
                        cancellationToken);
                    if (softRows == 0) throw new CrestConcurrencyException(typeof(TEntity).Name, id);
                }
                else
                {
                    var physicalDelete = _orm.Delete<TEntity>()
                        .Where(e => e.Id.Equals(id))
                        .Where(ConcurrencyStampPredicate(expectedStamp));
                    var rows = await WithTenant(physicalDelete).ExecuteAffrowsAsync(cancellationToken);
                    if (rows == 0) throw new CrestConcurrencyException(typeof(TEntity).Name, id);
                }
                _logger?.LogDebug("Deleted entity {EntityType} by id with concurrency check: {Id}", typeof(TEntity).Name, id);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error deleting entity {EntityType} by id with concurrency check: {Id}", typeof(TEntity).Name, id);
                throw;
            }
        }

        public override async Task DeleteRangeAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
        {
            try
            {
                ThrowIfBatchDeleteHasNoPerRowStamp();
                _logger?.LogDebug("Deleting many entities {EntityType}", typeof(TEntity).Name);
                var entityList = entities.ToList();
                var ids = entityList.Select(e => e.Id).ToList();
                if (ids.Count == 0)
                {
                    return;
                }

                if (typeof(ISoftDelete).IsAssignableFrom(typeof(TEntity)))
                {
                    await FreeSqlSoftDeleteOperation.ExecuteAsync(
                        WithTenant(_orm.Update<TEntity>()
                            .Where(e => ids.Contains(e.Id))
                            .Where(LiveRowPredicate())),
                        _currentUserProvider,
                        advanceConcurrencyStamp: false,
                        cancellationToken);
                }
                else
                {
                    await WithTenant(_orm.Delete<TEntity>().Where(e => ids.Contains(e.Id))).ExecuteAffrowsAsync(cancellationToken);
                }
                _logger?.LogDebug("Deleted {Count} entities {EntityType}", entityList.Count, typeof(TEntity).Name);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error deleting many entities {EntityType}", typeof(TEntity).Name);
                throw;
            }
        }

        public override async Task DeleteRangeAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
        {
            try
            {
                ThrowIfBatchDeleteHasNoPerRowStamp();
                _logger?.LogDebug("Deleting many entities {EntityType} by predicate", typeof(TEntity).Name);
                if (typeof(ISoftDelete).IsAssignableFrom(typeof(TEntity)))
                {
                    await FreeSqlSoftDeleteOperation.ExecuteAsync(
                        WithTenant(_orm.Update<TEntity>()
                            .Where(predicate)
                            .Where(LiveRowPredicate())),
                        _currentUserProvider,
                        advanceConcurrencyStamp: false,
                        cancellationToken);
                }
                else
                {
                    await WithTenant(_orm.Delete<TEntity>().Where(predicate)).ExecuteAffrowsAsync(cancellationToken);
                }
                _logger?.LogDebug("Deleted entities {EntityType} by predicate", typeof(TEntity).Name);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error deleting many entities {EntityType} by predicate", typeof(TEntity).Name);
                throw;
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

        public override async Task<PagedResult<TEntity>> GetPagedAsync(int pageIndex, int pageSize, CancellationToken cancellationToken = default)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                _logger?.LogDebug("Getting paged entities {EntityType} - Page {PageIndex}, Size {PageSize}", typeof(TEntity).Name, pageIndex, pageSize);
                var query = SelectForCurrentTenant();
                var total = await query.CountAsync(cancellationToken);
                var items = await query.Page(pageIndex + 1, pageSize).ToListAsync(cancellationToken);
                _logger?.LogDebug("Got {Count} entities {EntityType} out of {Total}", items.Count, typeof(TEntity).Name, total);
                return new PagedResult<TEntity>(items, (int)total, pageIndex, pageSize);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error getting paged entities {EntityType}", typeof(TEntity).Name);
                throw;
            }
        }

        public override async Task<PagedResult<TEntity>> GetPagedAsync(int pageIndex, int pageSize, Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                _logger?.LogDebug("Getting paged entities {EntityType} with predicate - Page {PageIndex}, Size {PageSize}", typeof(TEntity).Name, pageIndex, pageSize);
                var query = SelectForCurrentTenant().Where(predicate);
                var total = await query.CountAsync(cancellationToken);
                var items = await query.Page(pageIndex + 1, pageSize).ToListAsync(cancellationToken);
                _logger?.LogDebug("Got {Count} entities {EntityType} out of {Total}", items.Count, typeof(TEntity).Name, total);
                return new PagedResult<TEntity>(items, (int)total, pageIndex, pageSize);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error getting paged entities {EntityType} with predicate", typeof(TEntity).Name);
                throw;
            }
        }

        public override async Task<PagedResult<TEntity>> GetPagedAsync(int pageIndex, int pageSize, Expression<Func<TEntity, bool>> predicate, Expression<Func<TEntity, object>> orderBy, bool ascending = true, CancellationToken cancellationToken = default)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                _logger?.LogDebug("Getting paged entities {EntityType} with predicate and order - Page {PageIndex}, Size {PageSize}", typeof(TEntity).Name, pageIndex, pageSize);
                var query = SelectForCurrentTenant().Where(predicate);
                query = ascending ? query.OrderBy(orderBy) : query.OrderByDescending(orderBy);
                var total = await query.CountAsync(cancellationToken);
                var items = await query.Page(pageIndex + 1, pageSize).ToListAsync(cancellationToken);
                _logger?.LogDebug("Got {Count} entities {EntityType} out of {Total}", items.Count, typeof(TEntity).Name, total);
                return new PagedResult<TEntity>(items, (int)total, pageIndex, pageSize);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error getting paged entities {EntityType} with predicate and order", typeof(TEntity).Name);
                throw;
            }
        }

        public override async Task<PagedResult<TEntity>> GetPagedAsync(int pageIndex, int pageSize, Expression<Func<TEntity, object>> orderBy, bool ascending = true, CancellationToken cancellationToken = default)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                _logger?.LogDebug("Getting paged entities {EntityType} with order - Page {PageIndex}, Size {PageSize}", typeof(TEntity).Name, pageIndex, pageSize);
                var query = SelectForCurrentTenant();
                query = ascending ? query.OrderBy(orderBy) : query.OrderByDescending(orderBy);
                var total = await query.CountAsync(cancellationToken);
                var items = await query.Page(pageIndex + 1, pageSize).ToListAsync(cancellationToken);
                _logger?.LogDebug("Got {Count} entities {EntityType} out of {Total}", items.Count, typeof(TEntity).Name, total);
                return new PagedResult<TEntity>(items, (int)total, pageIndex, pageSize);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error getting paged entities {EntityType} with order", typeof(TEntity).Name);
                throw;
            }
        }

        public override async Task<long> GetCountAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                _logger?.LogDebug("Getting count of entities {EntityType}", typeof(TEntity).Name);
                var count = await SelectForCurrentTenant().CountAsync(cancellationToken);
                _logger?.LogDebug("Got count {Count} for entities {EntityType}", count, typeof(TEntity).Name);
                return count;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error getting count of entities {EntityType}", typeof(TEntity).Name);
                throw;
            }
        }

        public override async Task<long> GetCountAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                _logger?.LogDebug("Getting count of entities {EntityType} with predicate", typeof(TEntity).Name);
                var count = await SelectForCurrentTenant().Where(predicate).CountAsync(cancellationToken);
                _logger?.LogDebug("Got count {Count} for entities {EntityType} with predicate", count, typeof(TEntity).Name);
                return count;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error getting count of entities {EntityType} with predicate", typeof(TEntity).Name);
                throw;
            }
        }

        public override async Task<bool> AnyAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                _logger?.LogDebug("Checking if any entities {EntityType} exist", typeof(TEntity).Name);
                var result = await SelectForCurrentTenant().AnyAsync(cancellationToken);
                _logger?.LogDebug("Any entities {EntityType} exist: {Result}", typeof(TEntity).Name, result);
                return result;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error checking if any entities {EntityType} exist", typeof(TEntity).Name);
                throw;
            }
        }

        public override async Task<bool> AnyAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                _logger?.LogDebug("Checking if any entities {EntityType} match predicate", typeof(TEntity).Name);
                var result = await SelectForCurrentTenant().Where(predicate).AnyAsync(cancellationToken);
                _logger?.LogDebug("Any entities {EntityType} match predicate: {Result}", typeof(TEntity).Name, result);
                return result;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error checking if any entities {EntityType} match predicate", typeof(TEntity).Name);
                throw;
            }
        }

        public override async Task<bool> ExistsAsync(TKey id, CancellationToken cancellationToken = default)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                _logger?.LogDebug("Checking if entity {EntityType} with id {Id} exists", typeof(TEntity).Name, id);
                var result = await SelectForCurrentTenant().Where(e => e.Id.Equals(id)).AnyAsync(cancellationToken);
                _logger?.LogDebug("Entity {EntityType} with id {Id} exists: {Result}", typeof(TEntity).Name, id, result);
                return result;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error checking if entity {EntityType} with id {Id} exists", typeof(TEntity).Name, id);
                throw;
            }
        }
    }
}
