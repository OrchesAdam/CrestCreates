using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading.Tasks;
using FreeSql;
using CrestCreates.Domain.Entities;
using CrestCreates.Domain.Repositories;
using Microsoft.Extensions.Logging;

namespace CrestCreates.OrmProviders.FreeSqlProvider.Repositories
{
    /// <summary>
    /// FreeSql 仓储基类（支持 UnitOfWorkManager）
    /// </summary>
    /// <typeparam name="TEntity">实体类型</typeparam>
    /// <typeparam name="TKey">主键类型</typeparam>
    /// <remarks>
    /// 官方文档: https://freesql.net/guide/unitofwork-manager.html#扩展-重写仓储
    /// 此实现继承自 FreeSql 的 BaseRepository，并绑定到 UnitOfWorkManager
    /// </remarks>
    public abstract class FreeSqlRepository<TEntity, TKey> : BaseRepository<TEntity, TKey>, IRepository<TEntity, TKey>
        where TEntity : class, IEntity<TKey>
        where TKey : IEquatable<TKey>
    {
        protected readonly ILogger<FreeSqlRepository<TEntity, TKey>> _logger;

        /// <summary>
        /// 构造函数（支持 UnitOfWorkManager）
        /// </summary>
        /// <param name="uowManager">工作单元管理器</param>
        /// <param name="logger">日志记录器</param>
        public FreeSqlRepository(UnitOfWork.FreeSqlUnitOfWorkManager uowManager, ILogger<FreeSqlRepository<TEntity, TKey>> logger = null) : base(uowManager?.Orm)
        {
            _logger = logger;
            // 关键：将仓储绑定到 UnitOfWorkManager
            // 这样所有仓储操作都会自动参与到同一个事务中
            if (uowManager != null && this is FreeSql.IBaseRepository baseRepo)
            {
                uowManager.Binding(baseRepo);
            }
        }

        #region IRepository Implementation

        /// <summary>
        /// 根据 ID 获取实体
        /// </summary>
        public virtual async Task<TEntity> GetByIdAsync(TKey id)
        {
            try
            {
                _logger?.LogDebug("Getting entity {EntityType} by id: {Id}", typeof(TEntity).Name, id);
                var result = await Select.Where(e => e.Id.Equals(id)).FirstAsync();
                _logger?.LogDebug("Got entity {EntityType} by id: {Id}", typeof(TEntity).Name, id);
                return result;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error getting entity {EntityType} by id: {Id}", typeof(TEntity).Name, id);
                throw;
            }
        }

        /// <summary>
        /// 获取所有实体
        /// </summary>
        public virtual async Task<List<TEntity>> GetAllAsync()
        {
            try
            {
                _logger?.LogDebug("Getting all entities {EntityType}", typeof(TEntity).Name);
                var result = await Select.ToListAsync();
                _logger?.LogDebug("Got {Count} entities {EntityType}", result.Count, typeof(TEntity).Name);
                return result;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error getting all entities {EntityType}", typeof(TEntity).Name);
                throw;
            }
        }

        /// <summary>
        /// 添加实体
        /// </summary>
        public virtual async Task<TEntity> AddAsync(TEntity entity)
        {
            try
            {
                _logger?.LogDebug("Adding entity {EntityType}", typeof(TEntity).Name);
                await InsertAsync(entity);
                _logger?.LogDebug("Added entity {EntityType} with id: {Id}", typeof(TEntity).Name, entity.Id);
                return entity;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error adding entity {EntityType}", typeof(TEntity).Name);
                throw;
            }
        }

        /// <summary>
        /// 更新实体
        /// </summary>
        public virtual async Task<TEntity> UpdateAsync(TEntity entity)
        {
            try
            {
                _logger?.LogDebug("Updating entity {EntityType} with id: {Id}", typeof(TEntity).Name, entity.Id);
                await base.UpdateAsync(entity);
                _logger?.LogDebug("Updated entity {EntityType} with id: {Id}", typeof(TEntity).Name, entity.Id);
                return entity;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error updating entity {EntityType} with id: {Id}", typeof(TEntity).Name, entity.Id);
                throw;
            }
        }

        /// <summary>
        /// 删除实体
        /// </summary>
        public virtual async Task DeleteAsync(TEntity entity)
        {
            try
            {
                _logger?.LogDebug("Deleting entity {EntityType} with id: {Id}", typeof(TEntity).Name, entity.Id);
                await base.DeleteAsync(entity);
                _logger?.LogDebug("Deleted entity {EntityType} with id: {Id}", typeof(TEntity).Name, entity.Id);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error deleting entity {EntityType} with id: {Id}", typeof(TEntity).Name, entity.Id);
                throw;
            }
        }

        /// <summary>
        /// 根据条件查找实体
        /// </summary>
        public virtual async Task<List<TEntity>> FindAsync(Expression<Func<TEntity, bool>> predicate)
        {
            try
            {
                _logger?.LogDebug("Finding entities {EntityType}", typeof(TEntity).Name);
                var result = await Select.Where(predicate).ToListAsync();
                _logger?.LogDebug("Found {Count} entities {EntityType}", result.Count, typeof(TEntity).Name);
                return result;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error finding entities {EntityType}", typeof(TEntity).Name);
                throw;
            }
        }

        #endregion

        #region 扩展方法（利用 FreeSql BaseRepository 的功能）

        /// <summary>
        /// 批量插入
        /// </summary>
        public virtual async Task<int> InsertManyAsync(IEnumerable<TEntity> entities)
        {
            try
            {
                _logger?.LogDebug("Inserting many entities {EntityType}", typeof(TEntity).Name);
                var result = await Orm.Insert(entities).ExecuteAffrowsAsync();
                _logger?.LogDebug("Inserted {Count} entities {EntityType}", result, typeof(TEntity).Name);
                return result;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error inserting many entities {EntityType}", typeof(TEntity).Name);
                throw;
            }
        }

        /// <summary>
        /// 批量更新
        /// </summary>
        public virtual async Task<int> UpdateManyAsync(IEnumerable<TEntity> entities)
        {
            try
            {
                _logger?.LogDebug("Updating many entities {EntityType}", typeof(TEntity).Name);
                var result = await Orm.Update<TEntity>().SetSource(entities).ExecuteAffrowsAsync();
                _logger?.LogDebug("Updated {Count} entities {EntityType}", result, typeof(TEntity).Name);
                return result;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error updating many entities {EntityType}", typeof(TEntity).Name);
                throw;
            }
        }

        /// <summary>
        /// 批量删除
        /// </summary>
        public virtual async Task<int> DeleteManyAsync(Expression<Func<TEntity, bool>> predicate)
        {
            try
            {
                _logger?.LogDebug("Deleting many entities {EntityType}", typeof(TEntity).Name);
                var result = await Orm.Delete<TEntity>().Where(predicate).ExecuteAffrowsAsync();
                _logger?.LogDebug("Deleted {Count} entities {EntityType}", result, typeof(TEntity).Name);
                return result;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error deleting many entities {EntityType}", typeof(TEntity).Name);
                throw;
            }
        }

        /// <summary>
        /// 分页查询
        /// </summary>
        public virtual async Task<(List<TEntity> Items, long Total)> GetPagedAsync(
            int pageIndex, 
            int pageSize, 
            Expression<Func<TEntity, bool>> predicate = null)
        {
            try
            {
                _logger?.LogDebug("Getting paged entities {EntityType} - Page {PageIndex}, Size {PageSize}", typeof(TEntity).Name, pageIndex, pageSize);
                var query = Select;
                if (predicate != null)
                {
                    query = query.Where(predicate);
                }

                var total = await query.CountAsync();
                var items = await query.Page(pageIndex, pageSize).ToListAsync();
                _logger?.LogDebug("Got {Count} entities {EntityType} out of {Total}", items.Count, typeof(TEntity).Name, total);
                return (items, total);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error getting paged entities {EntityType}", typeof(TEntity).Name);
                throw;
            }
        }

        #endregion
    }
}
