using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading.Tasks;
using FreeSql;
using CrestCreates.Domain.Entities;
using CrestCreates.Domain.Repositories;

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
        /// <summary>
        /// 构造函数（支持 UnitOfWorkManager）
        /// </summary>
        /// <param name="uowManager">工作单元管理器</param>
        public FreeSqlRepository(UnitOfWork.FreeSqlUnitOfWorkManager uowManager) : base(uowManager?.Orm)
        {
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
            return await Select.Where(e => e.Id.Equals(id)).FirstAsync();
        }

        /// <summary>
        /// 获取所有实体
        /// </summary>
        public virtual async Task<List<TEntity>> GetAllAsync()
        {
            return await Select.ToListAsync();
        }

        /// <summary>
        /// 添加实体
        /// </summary>
        public virtual async Task<TEntity> AddAsync(TEntity entity)
        {
            await InsertAsync(entity);
            return entity;
        }

        /// <summary>
        /// 更新实体
        /// </summary>
        public virtual async Task<TEntity> UpdateAsync(TEntity entity)
        {
            await base.UpdateAsync(entity);
            return entity;
        }

        /// <summary>
        /// 删除实体
        /// </summary>
        public virtual async Task DeleteAsync(TEntity entity)
        {
            await base.DeleteAsync(entity);
        }

        /// <summary>
        /// 根据条件查找实体
        /// </summary>
        public virtual async Task<List<TEntity>> FindAsync(Expression<Func<TEntity, bool>> predicate)
        {
            return await Select.Where(predicate).ToListAsync();
        }

        #endregion

        #region 扩展方法（利用 FreeSql BaseRepository 的功能）

        /// <summary>
        /// 批量插入
        /// </summary>
        public virtual async Task<int> InsertManyAsync(IEnumerable<TEntity> entities)
        {
            return await Orm.Insert(entities).ExecuteAffrowsAsync();
        }

        /// <summary>
        /// 批量更新
        /// </summary>
        public virtual async Task<int> UpdateManyAsync(IEnumerable<TEntity> entities)
        {
            return await Orm.Update<TEntity>().SetSource(entities).ExecuteAffrowsAsync();
        }

        /// <summary>
        /// 批量删除
        /// </summary>
        public virtual async Task<int> DeleteManyAsync(Expression<Func<TEntity, bool>> predicate)
        {
            return await Orm.Delete<TEntity>().Where(predicate).ExecuteAffrowsAsync();
        }

        /// <summary>
        /// 分页查询
        /// </summary>
        public virtual async Task<(List<TEntity> Items, long Total)> GetPagedAsync(
            int pageIndex, 
            int pageSize, 
            Expression<Func<TEntity, bool>> predicate = null)
        {
            var query = Select;
            if (predicate != null)
            {
                query = query.Where(predicate);
            }

            var total = await query.CountAsync();
            var items = await query.Page(pageIndex, pageSize).ToListAsync();

            return (items, total);
        }

        #endregion
    }
}
