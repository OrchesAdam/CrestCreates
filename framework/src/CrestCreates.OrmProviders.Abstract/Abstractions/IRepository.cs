using System.Linq.Expressions;
using CrestCreates.DbContextProvider.Abstract;
using CrestCreates.Domain.Entities;
using CrestCreates.Domain.Shared.Entities;

namespace CrestCreates.OrmProviders.Abstract.Abstractions
{
    /// <summary>
    /// 仓储基础接口（不带主键类型）
    /// </summary>
    /// <typeparam name="TEntity">实体类型</typeparam>
    public interface IRepository<TEntity> where TEntity : class
    {
        #region 查询操作

        /// <summary>
        /// 获取所有实体
        /// </summary>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>实体列表</returns>
        Task<List<TEntity>> GetAllAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 根据条件查询实体
        /// </summary>
        /// <param name="predicate">查询条件</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>匹配的实体列表</returns>
        Task<List<TEntity>> FindAsync(Expression<Func<TEntity, bool>> predicate,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 根据条件查询单个实体
        /// </summary>
        /// <param name="predicate">查询条件</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>匹配的实体，如果不存在则返回 null</returns>
        Task<TEntity> FirstOrDefaultAsync(Expression<Func<TEntity, bool>> predicate,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 检查是否存在满足条件的实体
        /// </summary>
        /// <param name="predicate">查询条件</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>如果存在则返回 true，否则返回 false</returns>
        Task<bool> AnyAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default);

        /// <summary>
        /// 获取满足条件的实体数量
        /// </summary>
        /// <param name="predicate">查询条件，如果为 null 则计算所有实体</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>实体数量</returns>
        Task<int> CountAsync(Expression<Func<TEntity, bool>> predicate = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 获取满足条件的实体长整型数量
        /// </summary>
        /// <param name="predicate">查询条件，如果为 null 则计算所有实体</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>实体数量</returns>
        Task<long> LongCountAsync(Expression<Func<TEntity, bool>> predicate = null,
            CancellationToken cancellationToken = default);

        #endregion

        #region 新增操作

        /// <summary>
        /// 添加单个实体
        /// </summary>
        /// <param name="entity">要添加的实体</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>添加后的实体</returns>
        Task<TEntity> AddAsync(TEntity entity, CancellationToken cancellationToken = default);

        /// <summary>
        /// 批量添加实体
        /// </summary>
        /// <param name="entities">要添加的实体集合</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>受影响的行数</returns>
        Task AddRangeAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default);

        #endregion

        #region 更新操作

        /// <summary>
        /// 更新单个实体
        /// </summary>
        /// <param name="entity">要更新的实体</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>更新后的实体</returns>
        Task<TEntity> UpdateAsync(TEntity entity, CancellationToken cancellationToken = default);

        /// <summary>
        /// 批量更新实体
        /// </summary>
        /// <param name="entities">要更新的实体集合</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>受影响的行数</returns>
        Task<int> UpdateRangeAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default);

        #endregion

        #region 删除操作

        /// <summary>
        /// 删除单个实体
        /// </summary>
        /// <param name="entity">要删除的实体</param>
        /// <param name="cancellationToken">取消令牌</param>
        Task DeleteAsync(TEntity entity, CancellationToken cancellationToken = default);

        /// <summary>
        /// 批量删除实体
        /// </summary>
        /// <param name="entities">要删除的实体集合</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>受影响的行数</returns>
        Task<int> DeleteRangeAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default);

        /// <summary>
        /// 根据条件批量删除实体
        /// </summary>
        /// <param name="predicate">删除条件</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>受影响的行数</returns>
        Task<int> DeleteAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default);

        #endregion

        #region 高级查询

        /// <summary>
        /// 获取可查询对象
        /// </summary>
        /// <returns>查询构建器</returns>
        IQueryableBuilder<TEntity> AsQueryable();

        #endregion
    }

    /// <summary>
    /// 仓储基础接口（带主键类型）
    /// </summary>
    /// <typeparam name="TEntity">实体类型</typeparam>
    /// <typeparam name="TKey">主键类型</typeparam>
    public interface IRepository<TEntity, TKey> : IRepository<TEntity>
        where TEntity : class, IEntity<TKey>
        where TKey : IEquatable<TKey>
    {
        /// <summary>
        /// 根据主键获取实体
        /// </summary>
        /// <param name="id">主键值</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>找到的实体，如果不存在则返回 null</returns>
        Task<TEntity> GetByIdAsync(TKey id, CancellationToken cancellationToken = default);

        /// <summary>
        /// 根据主键集合获取实体列表
        /// </summary>
        /// <param name="ids">主键值集合</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>匹配的实体列表</returns>
        Task<List<TEntity>> GetByIdsAsync(IEnumerable<TKey> ids, CancellationToken cancellationToken = default);

        /// <summary>
        /// 根据主键删除实体
        /// </summary>
        /// <param name="id">主键值</param>
        /// <param name="cancellationToken">取消令牌</param>
        Task DeleteByIdAsync(TKey id, CancellationToken cancellationToken = default);

        /// <summary>
        /// 根据主键集合批量删除实体
        /// </summary>
        /// <param name="ids">主键值集合</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>受影响的行数</returns>
        Task<int> DeleteByIdsAsync(IEnumerable<TKey> ids, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// 只读仓储接口
    /// </summary>
    /// <typeparam name="TEntity">实体类型</typeparam>
    public interface IReadOnlyRepository<TEntity> where TEntity : class
    {
        /// <summary>
        /// 获取所有实体
        /// </summary>
        Task<List<TEntity>> GetAllAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 根据条件查询实体
        /// </summary>
        Task<List<TEntity>> FindAsync(Expression<Func<TEntity, bool>> predicate,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 根据条件查询单个实体
        /// </summary>
        Task<TEntity> FirstOrDefaultAsync(Expression<Func<TEntity, bool>> predicate,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 检查是否存在满足条件的实体
        /// </summary>
        Task<bool> AnyAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default);

        /// <summary>
        /// 获取满足条件的实体数量
        /// </summary>
        Task<int> CountAsync(Expression<Func<TEntity, bool>> predicate = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 获取可查询对象
        /// </summary>
        IQueryableBuilder<TEntity> AsQueryable();
    }

    /// <summary>
    /// 只读仓储接口（带主键）
    /// </summary>
    /// <typeparam name="TEntity">实体类型</typeparam>
    /// <typeparam name="TKey">主键类型</typeparam>
    public interface IReadOnlyRepository<TEntity, TKey> : IReadOnlyRepository<TEntity>
        where TEntity : class, IEntity<TKey>
        where TKey : IEquatable<TKey>
    {
        /// <summary>
        /// 根据主键获取实体
        /// </summary>
        Task<TEntity> GetByIdAsync(TKey id, CancellationToken cancellationToken = default);

        /// <summary>
        /// 根据主键集合获取实体列表
        /// </summary>
        Task<List<TEntity>> GetByIdsAsync(IEnumerable<TKey> ids, CancellationToken cancellationToken = default);
    }
}