using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using SqlSugar;
using CrestCreates.Data.Abstractions;
using CrestCreates.Domain.Entities;
using CrestCreates.Domain.Repositories;
using CrestCreates.Domain.Exceptions;
using CrestCreates.Domain.Shared.DTOs;
using CrestCreates.Domain.Shared.Entities;
using CrestCreates.Domain.Shared.Entities.Auditing;
using Microsoft.Extensions.Logging;

namespace CrestCreates.Data.SqlSugar.Repositories
{
    public abstract class SqlSugarRepository<TEntity, TKey> : CrestRepositoryBase<TEntity, TKey>
        where TEntity : class, IEntity<TKey>, new()
        where TKey : IEquatable<TKey>
    {
        protected readonly ISqlSugarClient _sqlSugarClient;
        protected readonly ILogger<SqlSugarRepository<TEntity, TKey>>? _logger;
        private readonly ICurrentUserProvider? _currentUserProvider;

        protected SqlSugarRepository(
            ISqlSugarClient sqlSugarClient,
            ILogger<SqlSugarRepository<TEntity, TKey>>? logger = null,
            ICurrentUserProvider? currentUserProvider = null)
        {
            _sqlSugarClient = sqlSugarClient;
            _logger = logger;
            _currentUserProvider = currentUserProvider;

            SqlSugarSoftDeleteFilterRegistration.EnsureRegistered(_sqlSugarClient, typeof(TEntity));
        }

        private string ColumnName(string clrName)
        {
            return _sqlSugarClient.EntityMaintenance.GetDbColumnName(clrName, typeof(TEntity));
        }

        public override IQueryable<TEntity> GetQueryable()
        {
            throw new NotSupportedException(
                "SqlSugar repositories do not expose a composable IQueryable. Use the asynchronous repository query methods or the SDK query API directly.");
        }

        private ISugarQueryable<TEntity> Queryable()
        {
            return _sqlSugarClient.Queryable<TEntity>();
        }

        public override async Task<List<TEntity>> GetListAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                _logger?.LogDebug("Getting all entities {EntityType}", typeof(TEntity).Name);
                var result = await Queryable()
                    .ToListAsync();
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
                var result = await Queryable()
                    .Where(predicate)
                    .ToListAsync();
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
                var query = Queryable().Where(predicate);
                query = ascending ? query.OrderBy(orderBy) : query.OrderByDescending(orderBy);
                var result = await query.ToListAsync();
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
                var result = await Queryable()
                    .InSingleAsync(id);
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
                var result = await Queryable()
                    .Where(predicate)
                    .FirstAsync();
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
                await _sqlSugarClient.Insertable(entity).IgnoreColumns("DomainEvents").ExecuteCommandAsync();
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
                await _sqlSugarClient.Insertable(entityList).IgnoreColumns("DomainEvents").ExecuteCommandAsync();
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
                    var rows = await _sqlSugarClient.Updateable(entity)
                        .IgnoreColumns("DomainEvents")
                        .Where(
                            $"{ColumnName("Id")} = @entityId AND {ColumnName(nameof(IHasConcurrencyStamp.ConcurrencyStamp))} = @oldStamp",
                            new { entityId = entity.Id, oldStamp })
                        .ExecuteCommandAsync();
                    if (rows == 0) throw new CrestConcurrencyException(typeof(TEntity).Name, entity.Id);
                }
                else
                {
                    await _sqlSugarClient.Updateable(entity).IgnoreColumns("DomainEvents").ExecuteCommandAsync();
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
                await _sqlSugarClient.Updateable(entityList).IgnoreColumns("DomainEvents").ExecuteCommandAsync();
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
                        var rows = await SqlSugarSoftDeleteOperation.ExecuteAsync(
                            _sqlSugarClient.Updateable<TEntity>().Where(
                                $"{ColumnName("Id")} = @entityId AND {ColumnName(nameof(IHasConcurrencyStamp.ConcurrencyStamp))} = @entityStamp AND {ColumnName(nameof(ISoftDelete.IsDeleted))} = 0",
                                new { entityId = entity.Id, entityStamp = stamp.ConcurrencyStamp }),
                            _currentUserProvider,
                            advanceConcurrencyStamp: true,
                            cancellationToken);
                        if (rows == 0) throw new CrestConcurrencyException(typeof(TEntity).Name, entity.Id);
                    }
                    else
                    {
                        await SqlSugarSoftDeleteOperation.ExecuteAsync(
                            _sqlSugarClient.Updateable<TEntity>().Where(
                                $"{ColumnName("Id")} = @entityId AND {ColumnName(nameof(ISoftDelete.IsDeleted))} = 0",
                                new { entityId = entity.Id }),
                            _currentUserProvider,
                            advanceConcurrencyStamp: false,
                            cancellationToken);
                    }
                }
                else if (entity is IHasConcurrencyStamp stamp)
                {
                    var rows = await _sqlSugarClient.Deleteable<TEntity>()
                        .Where(
                            $"{ColumnName("Id")} = @entityId AND {ColumnName(nameof(IHasConcurrencyStamp.ConcurrencyStamp))} = @entityStamp",
                            new { entityId = entity.Id, entityStamp = stamp.ConcurrencyStamp })
                        .ExecuteCommandAsync();
                    if (rows == 0) throw new CrestConcurrencyException(typeof(TEntity).Name, entity.Id);
                }
                else
                {
                    await _sqlSugarClient.Deleteable(entity).ExecuteCommandAsync();
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
                    await SqlSugarSoftDeleteOperation.ExecuteAsync(
                        _sqlSugarClient.Updateable<TEntity>().Where(
                            $"{ColumnName("Id")} = @entityId AND {ColumnName(nameof(ISoftDelete.IsDeleted))} = 0",
                            new { entityId = id }),
                        _currentUserProvider,
                        advanceConcurrencyStamp: typeof(IHasConcurrencyStamp).IsAssignableFrom(typeof(TEntity)),
                        cancellationToken);
                }
                else
                {
                    await _sqlSugarClient.Deleteable<TEntity>()
                        .In(id)
                        .ExecuteCommandAsync();
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
                    var softRows = await SqlSugarSoftDeleteOperation.ExecuteAsync(
                        _sqlSugarClient.Updateable<TEntity>().Where(
                            $"{ColumnName("Id")} = @entityId AND {ColumnName(nameof(IHasConcurrencyStamp.ConcurrencyStamp))} = @entityStamp AND {ColumnName(nameof(ISoftDelete.IsDeleted))} = 0",
                            new { entityId = id, entityStamp = expectedStamp }),
                        _currentUserProvider,
                        advanceConcurrencyStamp: true,
                        cancellationToken);
                    if (softRows == 0) throw new CrestConcurrencyException(typeof(TEntity).Name, id);
                }
                else
                {
                    var rows = await _sqlSugarClient.Deleteable<TEntity>()
                        .Where(
                            $"{ColumnName("Id")} = @entityId AND {ColumnName(nameof(IHasConcurrencyStamp.ConcurrencyStamp))} = @entityStamp",
                            new { entityId = id, entityStamp = expectedStamp })
                        .ExecuteCommandAsync();
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
                var ids = entityList.Select(entity => entity.Id).ToList();
                if (ids.Count == 0)
                {
                    return;
                }

                if (typeof(ISoftDelete).IsAssignableFrom(typeof(TEntity)))
                {
                    await SqlSugarSoftDeleteOperation.ExecuteAsync(
                        _sqlSugarClient.Updateable<TEntity>()
                            .Where(entity => ids.Contains(entity.Id))
                            .Where($"{ColumnName(nameof(ISoftDelete.IsDeleted))} = 0", new { }),
                        _currentUserProvider,
                        advanceConcurrencyStamp: false,
                        cancellationToken);
                }
                else
                {
                    await _sqlSugarClient.Deleteable(entityList)
                        .ExecuteCommandAsync();
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
                    await SqlSugarSoftDeleteOperation.ExecuteAsync(
                        _sqlSugarClient.Updateable<TEntity>()
                            .Where(predicate)
                            .Where($"{ColumnName(nameof(ISoftDelete.IsDeleted))} = 0", new { }),
                        _currentUserProvider,
                        advanceConcurrencyStamp: false,
                        cancellationToken);
                }
                else
                {
                    await _sqlSugarClient.Deleteable<TEntity>()
                        .Where(predicate)
                        .ExecuteCommandAsync();
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
                var total = await Queryable().CountAsync();
                var items = await Queryable()
                    .Skip(pageIndex * pageSize)
                    .Take(pageSize)
                    .ToListAsync();
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
                var total = await Queryable().Where(predicate).CountAsync();
                var items = await Queryable()
                    .Where(predicate)
                    .Skip(pageIndex * pageSize)
                    .Take(pageSize)
                    .ToListAsync();
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
                var query = Queryable().Where(predicate);
                query = ascending ? query.OrderBy(orderBy) : query.OrderByDescending(orderBy);
                var total = await query.CountAsync();
                var items = await query
                    .Skip(pageIndex * pageSize)
                    .Take(pageSize)
                    .ToListAsync();
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
                var query = Queryable();
                query = ascending ? query.OrderBy(orderBy) : query.OrderByDescending(orderBy);
                var total = await query.CountAsync();
                var items = await query
                    .Skip(pageIndex * pageSize)
                    .Take(pageSize)
                    .ToListAsync();
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
                var count = await Queryable().CountAsync();
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
                var count = await Queryable().Where(predicate).CountAsync();
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
                var result = await Queryable().AnyAsync();
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
                var result = await Queryable().Where(predicate).AnyAsync();
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
                var result = await Queryable().Where(e => e.Id.Equals(id)).AnyAsync();
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
