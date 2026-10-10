using System;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Domain.UnitOfWork;

namespace CrestCreates.Data.Abstractions
{
    /// <summary>
    /// 工作单元管理器接口：唯一执行内核入口。
    /// </summary>
    /// <remarks>
    /// 开始协议（调用方帧激活，见设计 §4.1）：<see cref="BeginScope"/> 是同步方法，
    /// 在调用方帧内完成受管载体登记与环境激活；<c>await scope.StartAsync(ct)</c> 异步打开
    /// Provider 事务。标准写法：
    /// <c>await using var scope = manager.BeginScope(options); await scope.StartAsync(ct);</c>
    /// 委托式业务优先使用 <see cref="ExecuteAsync{TResult}"/>（action 运行在内核异步帧内）。
    /// </remarks>
    public interface IUnitOfWorkManager
    {
        /// <summary>
        /// 获取当前活动的工作单元，如果当前调用链没有工作单元则返回 null
        /// </summary>
        IUnitOfWork? CurrentOrNull { get; }

        /// <summary>
        /// 获取当前活动的工作单元
        /// </summary>
        /// <exception cref="InvalidOperationException">当前没有活动的工作单元时抛出</exception>
        IUnitOfWork Current { get; }

        /// <summary>
        /// 同步建立受管载体（调用方帧激活；本身不做 I/O）。
        /// </summary>
        IUnitOfWorkScope BeginScope(UnitOfWorkOptions? options = null);

        /// <summary>
        /// 使用工作单元执行异步操作（内核语法糖；action 接收内核联动 token）。
        /// </summary>
        Task<TResult> ExecuteAsync<TResult>(
            Func<CancellationToken, Task<TResult>> action,
            UnitOfWorkOptions? options = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 使用工作单元执行同步操作（异步内核的同步等待包装，唯一实现）。
        /// </summary>
        TResult Execute<TResult>(
            Func<TResult> action,
            UnitOfWorkOptions? options = null,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// 状态化工作单元 scope：承载参与者完成、事务结果、通知结果与释放标志。
    /// </summary>
    public interface IUnitOfWorkScope : IDisposable, IAsyncDisposable
    {
        IUnitOfWork UnitOfWork { get; }

        /// <summary>是否 owner（顶层/隔离）；join 为 false。</summary>
        bool IsOwner { get; }

        bool IsTransactional { get; }

        /// <summary>参与状态；释放标志独立于本状态（<see cref="IsReleased"/>），释放不覆盖它。</summary>
        UnitOfWorkState State { get; }

        /// <summary>事务结果（owner 专用；不可被后续操作改写）。</summary>
        UnitOfWorkTransactionOutcome TransactionOutcome { get; }

        /// <summary>提交后通知结果（独立于事务结果）。</summary>
        UnitOfWorkNotificationOutcome NotificationOutcome { get; }

        /// <summary>释放标志；释放后最终结果仍可读，任何操作确定性拒绝。</summary>
        bool IsReleased { get; }

        /// <summary>内核联动执行 token（调用方 CT + 截止时间）。</summary>
        CancellationToken ExecutionToken { get; }

        /// <summary>
        /// 异步开始阶段：创建联动 token 并在事务模式下打开 Provider 事务。
        /// </summary>
        Task StartAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 完成：owner = 校验→flush→commit（确认即记录）→通知→清理；join = 记录参与者成功（幂等）。
        /// </summary>
        Task CompleteAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 回滚：owner = 未提交状态回滚 + 清理；join = 标记 owner rollback-only。
        /// </summary>
        Task RollbackAsync(CancellationToken cancellationToken = default);
    }
}
