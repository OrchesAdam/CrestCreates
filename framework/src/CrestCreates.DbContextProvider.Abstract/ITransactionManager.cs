using System.Data;

namespace CrestCreates.DbContextProvider.Abstract;

/// <summary>
/// 事务管理器接口
/// </summary>
/// <remarks>
/// 负责创建和管理数据库事务
/// 支持嵌套事务和事务传播
/// </remarks>
public interface ITransactionManager
{
    /// <summary>
    /// 开始新事务
    /// </summary>
    /// <param name="isolationLevel">事务隔离级别</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>数据库事务</returns>
    Task<IDataBaseTransaction> BeginTransactionAsync(
        IsolationLevel isolationLevel = IsolationLevel.ReadCommitted,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取当前活动事务
    /// </summary>
    IDataBaseTransaction CurrentTransaction { get; }

    /// <summary>
    /// 是否存在活动事务
    /// </summary>
    bool HasActiveTransaction { get; }

    /// <summary>
    /// 使用事务执行操作
    /// </summary>
    /// <typeparam name="TResult">返回值类型</typeparam>
    /// <param name="action">要执行的操作</param>
    /// <param name="isolationLevel">事务隔离级别</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>操作结果</returns>
    Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<Task<TResult>> action,
        IsolationLevel isolationLevel = IsolationLevel.ReadCommitted,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 使用事务执行操作（无返回值）
    /// </summary>
    /// <param name="action">要执行的操作</param>
    /// <param name="isolationLevel">事务隔离级别</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task ExecuteInTransactionAsync(
        Func<Task> action,
        IsolationLevel isolationLevel = IsolationLevel.ReadCommitted,
        CancellationToken cancellationToken = default);
}