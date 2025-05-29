using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

namespace CrestCreates.Data.Repository
{
    /// <summary>
    /// 通用仓储接口，提供基础的CRUD操作
    /// </summary>
    /// <typeparam name="TEntity">实体类型</typeparam>
    public interface IRepository<TEntity> where TEntity : class
    {
        #region 查询操作

        /// <summary>
        /// 根据主键获取实体
        /// </summary>
        /// <param name="id">主键</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>实体或null</returns>
        Task<TEntity?> GetByIdAsync(object id, CancellationToken cancellationToken = default);

        /// <summary>
        /// 根据条件获取第一个实体
        /// </summary>
        /// <param name="predicate">查询条件</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>实体或null</returns>
        Task<TEntity?> FirstOrDefaultAsync(Expression<Func<TEntity, bool>>? predicate = null, 
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 获取所有实体
        /// </summary>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>实体集合</returns>
        Task<IEnumerable<TEntity>> GetAllAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 根据条件获取实体集合
        /// </summary>
        /// <param name="predicate">查询条件</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>实体集合</returns>
        Task<IEnumerable<TEntity>> GetAsync(Expression<Func<TEntity, bool>> predicate,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 分页查询
        /// </summary>
        /// <param name="pageIndex">页索引（从0开始）</param>
        /// <param name="pageSize">页大小</param>
        /// <param name="predicate">查询条件</param>
        /// <param name="orderBy">排序</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>分页结果</returns>
        Task<IPagedResult<TEntity>> GetPagedAsync(int pageIndex, int pageSize,
            Expression<Func<TEntity, bool>>? predicate = null,
            Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>>? orderBy = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 检查实体是否存在
        /// </summary>
        /// <param name="predicate">查询条件</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>是否存在</returns>
        Task<bool> AnyAsync(Expression<Func<TEntity, bool>> predicate,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 获取符合条件的实体数量
        /// </summary>
        /// <param name="predicate">查询条件</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>数量</returns>
        Task<int> CountAsync(Expression<Func<TEntity, bool>>? predicate = null,
            CancellationToken cancellationToken = default);

        #endregion

        #region 写入操作

        /// <summary>
        /// 添加实体
        /// </summary>
        /// <param name="entity">实体</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>添加的实体</returns>
        Task<TEntity> AddAsync(TEntity entity, CancellationToken cancellationToken = default);

        /// <summary>
        /// 批量添加实体
        /// </summary>
        /// <param name="entities">实体集合</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>添加的实体集合</returns>
        Task<IEnumerable<TEntity>> AddRangeAsync(IEnumerable<TEntity> entities,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 更新实体
        /// </summary>
        /// <param name="entity">实体</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>更新的实体</returns>
        Task<TEntity> UpdateAsync(TEntity entity, CancellationToken cancellationToken = default);

        /// <summary>
        /// 批量更新实体
        /// </summary>
        /// <param name="entities">实体集合</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>更新的实体集合</returns>
        Task<IEnumerable<TEntity>> UpdateRangeAsync(IEnumerable<TEntity> entities,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 删除实体
        /// </summary>
        /// <param name="entity">实体</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>是否成功</returns>
        Task<bool> DeleteAsync(TEntity entity, CancellationToken cancellationToken = default);

        /// <summary>
        /// 根据主键删除实体
        /// </summary>
        /// <param name="id">主键</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>是否成功</returns>
        Task<bool> DeleteAsync(object id, CancellationToken cancellationToken = default);

        /// <summary>
        /// 根据条件删除实体
        /// </summary>
        /// <param name="predicate">删除条件</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>删除的数量</returns>
        Task<int> DeleteAsync(Expression<Func<TEntity, bool>> predicate,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 批量删除实体
        /// </summary>
        /// <param name="entities">实体集合</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>删除的数量</returns>
        Task<int> DeleteRangeAsync(IEnumerable<TEntity> entities,
            CancellationToken cancellationToken = default);

        #endregion

        #region 查询构建器

        /// <summary>
        /// 获取可查询对象
        /// </summary>
        /// <returns>IQueryable对象</returns>
        IQueryable<TEntity> Query();

        /// <summary>
        /// 获取可查询对象（带条件）
        /// </summary>
        /// <param name="predicate">查询条件</param>
        /// <returns>IQueryable对象</returns>
        IQueryable<TEntity> Query(Expression<Func<TEntity, bool>> predicate);

        #endregion
    }
}
