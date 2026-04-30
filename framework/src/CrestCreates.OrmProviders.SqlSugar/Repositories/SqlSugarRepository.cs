using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using SqlSugar;
using CrestCreates.Domain.Entities;
using CrestCreates.Domain.Repositories;
using CrestCreates.Domain.Exceptions;
using CrestCreates.Domain.Shared.DTOs;
using CrestCreates.Domain.Shared.Entities;
using CrestCreates.Domain.Shared.Entities.Auditing;
using Microsoft.Extensions.Logging;

namespace CrestCreates.OrmProviders.SqlSugar.Repositories
{
    public abstract class SqlSugarRepository<TEntity, TKey> : CrestRepositoryBase<TEntity, TKey>
        where TEntity : class, IEntity<TKey>, new()
        where TKey : IEquatable<TKey>
    {
        protected readonly ISqlSugarClient _sqlSugarClient;
        protected readonly ILogger<SqlSugarRepository<TEntity, TKey>> _logger;

        protected SqlSugarRepository(ISqlSugarClient sqlSugarClient, ILogger<SqlSugarRepository<TEntity, TKey>> logger = null)
        {
            _sqlSugarClient = sqlSugarClient;
            _logger = logger;
        }

        public override IQueryable<TEntity> GetQueryable()
        {
            return _sqlSugarClient.Queryable<TEntity>().ToList().AsQueryable();
        }

        public override async Task<List<TEntity>> GetListAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                _logger?.LogDebug("Getting all entities {EntityType}", typeof(TEntity).Name);
                var result = await _sqlSugarClient.Queryable<TEntity>()
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
                var result = await _sqlSugarClient.Queryable<TEntity>()
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
                var query = _sqlSugarClient.Queryable<TEntity>().Where(predicate);
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
                var result = await _sqlSugarClient.Queryable<TEntity>()
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
                var result = await _sqlSugarClient.Queryable<TEntity>()
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
                        .Where("Id = @entityId AND ConcurrencyStamp = @oldStamp", new { entityId = entity.Id, oldStamp })
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
                await _sqlSugarClient.Deleteable(entity)
                    .ExecuteCommandAsync();
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
                await _sqlSugarClient.Deleteable<TEntity>()
                    .In(id)
                    .ExecuteCommandAsync();
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
                _logger?.LogDebug("Deleting entity {EntityType} by id with concurrency check: {Id}", typeof(TEntity).Name, id);
                var rows = await _sqlSugarClient.Deleteable<TEntity>()
                    .Where("Id = @Id AND ConcurrencyStamp = @Stamp", new { Id = id, Stamp = expectedStamp })
                    .ExecuteCommandAsync();
                if (rows == 0) throw new CrestConcurrencyException(typeof(TEntity).Name, id);
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
                _logger?.LogDebug("Deleting many entities {EntityType}", typeof(TEntity).Name);
                var entityList = entities.ToList();
                await _sqlSugarClient.Deleteable(entityList)
                    .ExecuteCommandAsync();
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
                _logger?.LogDebug("Deleting many entities {EntityType} by predicate", typeof(TEntity).Name);
                await _sqlSugarClient.Deleteable<TEntity>()
                    .Where(predicate)
                    .ExecuteCommandAsync();
                _logger?.LogDebug("Deleted entities {EntityType} by predicate", typeof(TEntity).Name);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error deleting many entities {EntityType} by predicate", typeof(TEntity).Name);
                throw;
            }
        }

        public override async Task<PagedResult<TEntity>> GetPagedAsync(int pageIndex, int pageSize, CancellationToken cancellationToken = default)
        {
            try
            {
                _logger?.LogDebug("Getting paged entities {EntityType} - Page {PageIndex}, Size {PageSize}", typeof(TEntity).Name, pageIndex, pageSize);
                var total = await _sqlSugarClient.Queryable<TEntity>().CountAsync();
                var items = await _sqlSugarClient.Queryable<TEntity>()
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
                var total = await _sqlSugarClient.Queryable<TEntity>().Where(predicate).CountAsync();
                var items = await _sqlSugarClient.Queryable<TEntity>()
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
                var query = _sqlSugarClient.Queryable<TEntity>().Where(predicate);
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
                var query = _sqlSugarClient.Queryable<TEntity>();
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
                var count = await _sqlSugarClient.Queryable<TEntity>().CountAsync();
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
                var count = await _sqlSugarClient.Queryable<TEntity>().Where(predicate).CountAsync();
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
                var result = await _sqlSugarClient.Queryable<TEntity>().AnyAsync();
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
                var result = await _sqlSugarClient.Queryable<TEntity>().Where(predicate).AnyAsync();
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
                var result = await _sqlSugarClient.Queryable<TEntity>().Where(e => e.Id.Equals(id)).AnyAsync();
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
