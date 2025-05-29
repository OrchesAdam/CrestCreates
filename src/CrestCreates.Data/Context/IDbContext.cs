using System;
using System.Threading;
using System.Threading.Tasks;

namespace CrestCreates.Data.Context
{
    /// <summary>
    /// 数据库上下文抽象接口
    /// </summary>
    public interface IDbContext : IDisposable
    {
        /// <summary>
        /// 获取实体集合
        /// </summary>
        /// <typeparam name="TEntity">实体类型</typeparam>
        /// <returns>实体集合</returns>
        IDbSet<TEntity> Set<TEntity>() where TEntity : class;

        /// <summary>
        /// 保存变更
        /// </summary>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>受影响的行数</returns>
        Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 同步保存变更
        /// </summary>
        /// <returns>受影响的行数</returns>
        int SaveChanges();

        /// <summary>
        /// 开始事务
        /// </summary>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>数据库事务</returns>
        Task<IDbTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 获取当前事务
        /// </summary>
        IDbTransaction? CurrentTransaction { get; }

        /// <summary>
        /// 检查数据库连接
        /// </summary>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>是否连接成功</returns>
        Task<bool> CanConnectAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 执行原始SQL查询
        /// </summary>
        /// <typeparam name="T">返回类型</typeparam>
        /// <param name="sql">SQL语句</param>
        /// <param name="parameters">参数</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>查询结果</returns>
        Task<T[]> ExecuteQueryAsync<T>(string sql, object[]? parameters = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 执行原始SQL命令
        /// </summary>
        /// <param name="sql">SQL语句</param>
        /// <param name="parameters">参数</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>受影响的行数</returns>
        Task<int> ExecuteCommandAsync(string sql, object[]? parameters = null,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// 数据库事务接口
    /// </summary>
    public interface IDbTransaction : IDisposable
    {
        /// <summary>
        /// 提交事务
        /// </summary>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>Task</returns>
        Task CommitAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 回滚事务
        /// </summary>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>Task</returns>
        Task RollbackAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 事务ID
        /// </summary>
        Guid TransactionId { get; }
    }

    /// <summary>
    /// 数据库集合接口
    /// </summary>
    /// <typeparam name="TEntity">实体类型</typeparam>
    public interface IDbSet<TEntity> where TEntity : class
    {
        /// <summary>
        /// 添加实体
        /// </summary>
        /// <param name="entity">实体</param>
        void Add(TEntity entity);

        /// <summary>
        /// 添加多个实体
        /// </summary>
        /// <param name="entities">实体集合</param>
        void AddRange(System.Collections.Generic.IEnumerable<TEntity> entities);

        /// <summary>
        /// 更新实体
        /// </summary>
        /// <param name="entity">实体</param>
        void Update(TEntity entity);

        /// <summary>
        /// 更新多个实体
        /// </summary>
        /// <param name="entities">实体集合</param>
        void UpdateRange(System.Collections.Generic.IEnumerable<TEntity> entities);

        /// <summary>
        /// 删除实体
        /// </summary>
        /// <param name="entity">实体</param>
        void Remove(TEntity entity);

        /// <summary>
        /// 删除多个实体
        /// </summary>
        /// <param name="entities">实体集合</param>
        void RemoveRange(System.Collections.Generic.IEnumerable<TEntity> entities);

        /// <summary>
        /// 根据主键查找实体
        /// </summary>
        /// <param name="keyValues">主键值</param>
        /// <returns>实体或null</returns>
        TEntity? Find(params object[] keyValues);

        /// <summary>
        /// 异步根据主键查找实体
        /// </summary>
        /// <param name="keyValues">主键值</param>
        /// <returns>实体或null</returns>
        Task<TEntity?> FindAsync(params object[] keyValues);

        /// <summary>
        /// 异步根据主键查找实体
        /// </summary>
        /// <param name="cancellationToken">取消令牌</param>
        /// <param name="keyValues">主键值</param>
        /// <returns>实体或null</returns>
        Task<TEntity?> FindAsync(CancellationToken cancellationToken, params object[] keyValues);

        /// <summary>
        /// 获取可查询对象
        /// </summary>
        /// <returns>IQueryable对象</returns>
        System.Linq.IQueryable<TEntity> AsQueryable();
    }
}
