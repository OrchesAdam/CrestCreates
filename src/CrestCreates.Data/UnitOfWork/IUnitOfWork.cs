using System;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Data.Context;
using CrestCreates.Data.Repository;

namespace CrestCreates.Data.UnitOfWork
{
    /// <summary>
    /// 工作单元接口
    /// </summary>
    public interface IUnitOfWork : IDisposable
    {
        /// <summary>
        /// 获取指定实体的仓储
        /// </summary>
        /// <typeparam name="TEntity">实体类型</typeparam>
        /// <returns>仓储实例</returns>
        IRepository<TEntity> GetRepository<TEntity>() where TEntity : class;

        /// <summary>
        /// 获取数据库上下文
        /// </summary>
        IDbContext DbContext { get; }

        /// <summary>
        /// 提交所有变更
        /// </summary>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>受影响的行数</returns>
        Task<int> CommitAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 同步提交所有变更
        /// </summary>
        /// <returns>受影响的行数</returns>
        int Commit();

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
        /// 提交事务
        /// </summary>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>Task</returns>
        Task CommitTransactionAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 回滚事务
        /// </summary>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>Task</returns>
        Task RollbackTransactionAsync(CancellationToken cancellationToken = default);

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

        /// <summary>
        /// 检查数据库连接
        /// </summary>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>是否连接成功</returns>
        Task<bool> CanConnectAsync(CancellationToken cancellationToken = default);
    }
}
