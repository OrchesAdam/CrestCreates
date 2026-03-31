using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using SqlSugar;
using CrestCreates.Domain.Entities;
using CrestCreates.Domain.Repositories;
using Microsoft.Extensions.Logging;

namespace CrestCreates.OrmProviders.SqlSugar.Repositories
{
    public abstract class SqlSugarRepository<TEntity, TKey> : IRepository<TEntity, TKey>
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

        public virtual async Task<TEntity> GetByIdAsync(TKey id)
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

        public virtual async Task<List<TEntity>> GetAllAsync()
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

        public virtual async Task<TEntity> AddAsync(TEntity entity)
        {
            try
            {
                _logger?.LogDebug("Adding entity {EntityType}", typeof(TEntity).Name);
                // 忽略DomainEvents属性，因为它是一个集合，无法直接映射到数据库
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

        public virtual async Task<List<TEntity>> FindAsync(Expression<Func<TEntity, bool>> predicate)
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

        public virtual async Task<TEntity> UpdateAsync(TEntity entity)
        {
            try
            {
                _logger?.LogDebug("Updating entity {EntityType} with id: {Id}", typeof(TEntity).Name, entity.Id);
                // 忽略DomainEvents属性，因为它是一个集合，无法直接映射到数据库
                await _sqlSugarClient.Updateable(entity).IgnoreColumns("DomainEvents").ExecuteCommandAsync();
                _logger?.LogDebug("Updated entity {EntityType} with id: {Id}", typeof(TEntity).Name, entity.Id);
                return entity;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error updating entity {EntityType} with id: {Id}", typeof(TEntity).Name, entity.Id);
                throw;
            }
        }

        public virtual async Task DeleteAsync(TEntity entity)
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
    }
}