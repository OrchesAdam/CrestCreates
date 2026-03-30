using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

namespace CrestCreates.DbContextProvider.Abstract
{
    /// <summary>
    /// 数据库实体集统一抽象接口
    /// </summary>
    /// <typeparam name="TEntity">实体类型</typeparam>
    /// <remarks>
    /// 提供对实体集合的基本操作，类似于 EF Core 的 DbSet
    /// 支持添加、更新、删除等操作
    /// </remarks>
    public interface IDataBaseSet<TEntity> where TEntity : class
    {
        /// <summary>
        /// 添加单个实体
        /// </summary>
        /// <param name="entity">要添加的实体</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>添加的实体</returns>
        Task<TEntity> AddAsync(TEntity entity, CancellationToken cancellationToken = default);

        /// <summary>
        /// 批量添加实体
        /// </summary>
        /// <param name="entities">要添加的实体集合</param>
        /// <param name="cancellationToken">取消令牌</param>
        Task AddRangeAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default);

        /// <summary>
        /// 更新实体
        /// </summary>
        /// <param name="entity">要更新的实体</param>
        void Update(TEntity entity);
        
        /// <summary>
        /// 异步更新实体
        /// </summary>
        /// <param name="entity"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        Task<TEntity> UpdateAsync(TEntity entity, CancellationToken cancellationToken = default);

        /// <summary>
        /// 批量更新实体
        /// </summary>
        /// <param name="entities">要更新的实体集合</param>
        void UpdateRange(IEnumerable<TEntity> entities);
        
        /// <summary>
        /// 异步批量更新实体
        /// </summary>
        /// <param name="entities"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        Task<int> UpdateRangeAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default);

        /// <summary>
        /// 删除实体
        /// </summary>
        /// <param name="entity">要删除的实体</param>
        void Remove(TEntity entity);
        
        /// <summary>
        /// 异步删除实体
        /// </summary>
        /// <param name="entity"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        Task RemoveAsync(TEntity entity, CancellationToken cancellationToken = default);

        /// <summary>
        /// 批量删除实体
        /// </summary>
        /// <param name="entities">要删除的实体集合</param>
        void RemoveRange(IEnumerable<TEntity> entities);
        
        /// <summary>
        /// 异步批量删除实体
        /// </summary>
        /// <param name="entities"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        Task<int> RemoveRangeAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default);


        /// <summary>
        /// 根据条件删除实体
        /// </summary>
        /// <param name="predicate"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        Task<int> RemoveRangeAsync(Expression<Func<TEntity, bool>> predicate,
            CancellationToken cancellationToken = default);
        
        /// <summary>
        /// 根据主键查找实体
        /// </summary>
        /// <param name="keyValues">主键值</param>
        /// <returns>找到的实体，如果不存在则返回 null</returns>
        Task<TEntity?> FindAsync(params object[] keyValues);

        /// <summary>
        /// 根据主键查找实体
        /// </summary>
        /// <param name="cancellationToken">取消令牌</param>
        /// <param name="keyValues">主键值</param>
        /// <returns>找到的实体，如果不存在则返回 null</returns>
        Task<TEntity?> FindAsync(CancellationToken cancellationToken, params object[] keyValues);

        /// <summary>
        /// 附加实体到上下文
        /// </summary>
        /// <param name="entity">要附加的实体</param>
        void Attach(TEntity entity);

        /// <summary>
        /// 批量附加实体到上下文
        /// </summary>
        /// <param name="entities">要附加的实体集合</param>
        void AttachRange(IEnumerable<TEntity> entities);
    }
}
