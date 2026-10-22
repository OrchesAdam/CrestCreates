using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using FreeSql;
using CrestCreates.Data.Abstractions;
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

        /// <summary>
        /// 构造函数（支持 UnitOfWorkManager）
        /// </summary>
        /// <param name="uowManager">工作单元管理器</param>
        /// <param name="logger">日志记录器</param>
        public FreeSqlRepository(
            UnitOfWork.FreeSqlUnitOfWorkManager uowManager,
            ILogger<FreeSqlRepository<TEntity, TKey>>? logger = null,
            ICurrentUserProvider? currentUserProvider = null)
        {
            _orm = uowManager.Orm ?? throw new InvalidOperationException("UnitOfWorkManager.Orm must not be null.");
            _logger = logger;
            _currentUserProvider = currentUserProvider;

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

        public override IQueryable<TEntity> GetQueryable()
        {
            throw new NotSupportedException(
                "FreeSql repositories do not expose a composable IQueryable. Use the asynchronous repository query methods or the SDK query API directly.");
        }

        public override async Task<List<TEntity>> GetListAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                _logger?.LogDebug("Getting all entities {EntityType}", typeof(TEntity).Name);
                var result = await _orm.Select<TEntity>().ToListAsync(cancellationToken);
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
                _logger?.LogDebug("Finding entities {EntityType}", typeof(TEntity).Name);
                var result = await _orm.Select<TEntity>().Where(predicate).ToListAsync(cancellationToken);
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
                _logger?.LogDebug("Finding entities {EntityType} with order", typeof(TEntity).Name);
                var query = _orm.Select<TEntity>().Where(predicate);
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
                _logger?.LogDebug("Getting entity {EntityType} by id: {Id}", typeof(TEntity).Name, id);
                var result = await _orm.Select<TEntity>().Where(e => e.Id.Equals(id)).FirstAsync(cancellationToken);
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
                _logger?.LogDebug("Getting entity {EntityType} by predicate", typeof(TEntity).Name);
                var result = await _orm.Select<TEntity>().Where(predicate).FirstAsync(cancellationToken);
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
                _logger?.LogDebug("Adding entity {EntityType}", typeof(TEntity).Name);
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
                _logger?.LogDebug("Inserting many entities {EntityType}", typeof(TEntity).Name);
                var entityList = entities.ToList();
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
                _logger?.LogDebug("Updating entity {EntityType} with id: {Id}", typeof(TEntity).Name, entity.Id);
                if (entity is IHasConcurrencyStamp stamp)
                {
                    var oldStamp = stamp.ConcurrencyStamp;
                    stamp.ConcurrencyStamp = Guid.NewGuid().ToString();
                    var rows = await _orm.Update<TEntity>()
                        .SetSource(entity)
                        .Where(e => e.Id.Equals(entity.Id))
                        .Where(ConcurrencyStampPredicate(oldStamp))
                        .ExecuteAffrowsAsync(cancellationToken);
                    if (rows == 0) throw new CrestConcurrencyException(typeof(TEntity).Name, entity.Id);
                }
                else
                {
                    await _orm.Update<TEntity>().SetSource(entity).ExecuteAffrowsAsync(cancellationToken);
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
                _logger?.LogDebug("Updating many entities {EntityType}", typeof(TEntity).Name);
                var entityList = entities.ToList();
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
                _logger?.LogDebug("Deleting entity {EntityType} with id: {Id}", typeof(TEntity).Name, entity.Id);
                if (entity is ISoftDelete)
                {
                    if (entity is IHasConcurrencyStamp stamp)
                    {
                        var rows = await FreeSqlSoftDeleteOperation.ExecuteAsync(
                            _orm.Update<TEntity>()
                                .Where(e => e.Id.Equals(entity.Id))
                                .Where(ConcurrencyStampPredicate(stamp.ConcurrencyStamp))
                                .Where(LiveRowPredicate()),
                            _currentUserProvider,
                            advanceConcurrencyStamp: true,
                            cancellationToken);
                        if (rows == 0) throw new CrestConcurrencyException(typeof(TEntity).Name, entity.Id);
                    }
                    else
                    {
                        await FreeSqlSoftDeleteOperation.ExecuteAsync(
                            _orm.Update<TEntity>()
                                .Where(e => e.Id.Equals(entity.Id))
                                .Where(LiveRowPredicate()),
                            _currentUserProvider,
                            advanceConcurrencyStamp: false,
                            cancellationToken);
                    }
                }
                else if (entity is IHasConcurrencyStamp stamp)
                {
                    var rows = await _orm.Delete<TEntity>()
                        .Where(e => e.Id.Equals(entity.Id))
                        .Where(ConcurrencyStampPredicate(stamp.ConcurrencyStamp))
                        .ExecuteAffrowsAsync(cancellationToken);
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
                _logger?.LogDebug("Deleting entity {EntityType} by id: {Id}", typeof(TEntity).Name, id);
                if (typeof(ISoftDelete).IsAssignableFrom(typeof(TEntity)))
                {
                    await FreeSqlSoftDeleteOperation.ExecuteAsync(
                        _orm.Update<TEntity>()
                            .Where(e => e.Id.Equals(id))
                            .Where(LiveRowPredicate()),
                        _currentUserProvider,
                        advanceConcurrencyStamp: typeof(IHasConcurrencyStamp).IsAssignableFrom(typeof(TEntity)),
                        cancellationToken);
                }
                else
                {
                    await _orm.Delete<TEntity>().Where(e => e.Id.Equals(id)).ExecuteAffrowsAsync(cancellationToken);
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
                if (typeof(ISoftDelete).IsAssignableFrom(typeof(TEntity)))
                {
                    var softRows = await FreeSqlSoftDeleteOperation.ExecuteAsync(
                        _orm.Update<TEntity>()
                            .Where(e => e.Id.Equals(id))
                            .Where(ConcurrencyStampPredicate(expectedStamp))
                            .Where(LiveRowPredicate()),
                        _currentUserProvider,
                        advanceConcurrencyStamp: true,
                        cancellationToken);
                    if (softRows == 0) throw new CrestConcurrencyException(typeof(TEntity).Name, id);
                }
                else
                {
                    var rows = await _orm.Delete<TEntity>()
                        .Where(e => e.Id.Equals(id))
                        .Where(ConcurrencyStampPredicate(expectedStamp))
                        .ExecuteAffrowsAsync(cancellationToken);
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
                        _orm.Update<TEntity>()
                            .Where(e => ids.Contains(e.Id))
                            .Where(LiveRowPredicate()),
                        _currentUserProvider,
                        advanceConcurrencyStamp: false,
                        cancellationToken);
                }
                else
                {
                    await _orm.Delete<TEntity>().Where(e => ids.Contains(e.Id)).ExecuteAffrowsAsync(cancellationToken);
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
                        _orm.Update<TEntity>()
                            .Where(predicate)
                            .Where(LiveRowPredicate()),
                        _currentUserProvider,
                        advanceConcurrencyStamp: false,
                        cancellationToken);
                }
                else
                {
                    await _orm.Delete<TEntity>().Where(predicate).ExecuteAffrowsAsync(cancellationToken);
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
                _logger?.LogDebug("Getting paged entities {EntityType} - Page {PageIndex}, Size {PageSize}", typeof(TEntity).Name, pageIndex, pageSize);
                var query = _orm.Select<TEntity>();
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
                _logger?.LogDebug("Getting paged entities {EntityType} with predicate - Page {PageIndex}, Size {PageSize}", typeof(TEntity).Name, pageIndex, pageSize);
                var query = _orm.Select<TEntity>().Where(predicate);
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
                _logger?.LogDebug("Getting paged entities {EntityType} with predicate and order - Page {PageIndex}, Size {PageSize}", typeof(TEntity).Name, pageIndex, pageSize);
                var query = _orm.Select<TEntity>().Where(predicate);
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
                _logger?.LogDebug("Getting paged entities {EntityType} with order - Page {PageIndex}, Size {PageSize}", typeof(TEntity).Name, pageIndex, pageSize);
                var query = _orm.Select<TEntity>();
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
                _logger?.LogDebug("Getting count of entities {EntityType}", typeof(TEntity).Name);
                var count = await _orm.Select<TEntity>().CountAsync(cancellationToken);
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
                _logger?.LogDebug("Getting count of entities {EntityType} with predicate", typeof(TEntity).Name);
                var count = await _orm.Select<TEntity>().Where(predicate).CountAsync(cancellationToken);
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
                _logger?.LogDebug("Checking if any entities {EntityType} exist", typeof(TEntity).Name);
                var result = await _orm.Select<TEntity>().AnyAsync(cancellationToken);
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
                _logger?.LogDebug("Checking if any entities {EntityType} match predicate", typeof(TEntity).Name);
                var result = await _orm.Select<TEntity>().Where(predicate).AnyAsync(cancellationToken);
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
                _logger?.LogDebug("Checking if entity {EntityType} with id {Id} exists", typeof(TEntity).Name, id);
                var result = await _orm.Select<TEntity>().Where(e => e.Id.Equals(id)).AnyAsync(cancellationToken);
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
