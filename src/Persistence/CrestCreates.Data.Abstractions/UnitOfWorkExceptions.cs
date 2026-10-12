using System;
using System.Collections.Generic;
using System.Linq;

namespace CrestCreates.Data.Abstractions
{
    /// <summary>
    /// owner 完成时发现被标记 rollback-only（有参与者失败/未完成退出）时抛出。
    /// 抛出前已完成回滚，不得静默提交部分失败。
    /// </summary>
    public sealed class UnitOfWorkRollbackOnlyException : InvalidOperationException
    {
        public UnitOfWorkRollbackOnlyException(string message)
            : base(message)
        {
        }
    }

    /// <summary>
    /// 数据库提交已确认，但提交后通知（域事件发布等）失败。
    /// 事务已提交的事实保留：不回滚、不自动重试业务；失败可检查。
    /// </summary>
    public sealed class UnitOfWorkPostCommitNotificationException : Exception
    {
        public UnitOfWorkPostCommitNotificationException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }

    /// <summary>
    /// 数据库提交**已确认**，但事务句柄清理（Dispose）失败。
    /// 由 Provider 在「提交确认后清理失败」时抛出；内核据此固定 Committed、
    /// 记录 Failed（清理未完成），不得把已确认事务改写为 Unknown。
    /// </summary>
    public sealed class UnitOfWorkCommitCleanupFailureException : InvalidOperationException
    {
        public UnitOfWorkCommitCleanupFailureException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }

    /// <summary>
    /// Provider 终结未完成事务时**回滚无法确认**（终结失败）。
    /// 原始终结故障保留为 <see cref="Exception.InnerException"/>；句柄释放/丢弃等后续失败
    /// 经 <see cref="SubsequentFailures"/> 保留。内核据此不得宣称 RolledBack
    /// （事务结果转 Unknown、参与状态 Failed），不得吞掉或替换本故障。
    /// </summary>
    public sealed class UnitOfWorkAbandonFailureException : InvalidOperationException
    {
        public UnitOfWorkAbandonFailureException(
            string message,
            Exception terminationFailure,
            IReadOnlyList<Exception>? subsequentFailures = null)
            : base(message, terminationFailure)
        {
            SubsequentFailures = subsequentFailures ?? Array.Empty<Exception>();
        }

        /// <summary>回滚失败之后的句柄释放/丢弃等次级失败（尽力完成，逐一保留）。</summary>
        public IReadOnlyList<Exception> SubsequentFailures { get; }
    }

    /// <summary>
    /// Provider 终结未完成事务时回滚**已确认**，但句柄释放/未 flush 丢弃失败（次级清理失败）。
    /// 内核保持 RolledBack 事务结果、记录 Failed 参与状态（清理未完成），与提交后的
    /// <see cref="UnitOfWorkCommitCleanupFailureException"/> 语义对称。
    /// </summary>
    public sealed class UnitOfWorkAbandonCleanupFailureException : InvalidOperationException
    {
        public UnitOfWorkAbandonCleanupFailureException(
            string message,
            IReadOnlyList<Exception> failures)
            : base(message, failures.Count > 0 ? failures[0] : null)
        {
            Failures = failures;
        }

        /// <summary>回滚已确认后的次级清理失败集合（逐一保留）。</summary>
        public IReadOnlyList<Exception> Failures { get; }
    }

    /// <summary>
    /// 释放（Dispose）阶段的次级失败聚合：回滚/环境恢复/资源释放按阶段收集，
    /// 各步骤尽可能完成；存在业务主异常时由入口抑制传播（次级结果保留），
    /// 无主异常时抛出以便调用方可检查。
    /// </summary>
    public sealed class UnitOfWorkCleanupException : Exception
    {
        public UnitOfWorkCleanupException(IReadOnlyList<UnitOfWorkCleanupFailure> failures)
            : base(BuildMessage(failures), failures.Count > 0 ? failures[0].Exception : null)
        {
            Failures = failures;
        }

        /// <summary>带阶段的次级失败集合。</summary>
        public IReadOnlyList<UnitOfWorkCleanupFailure> Failures { get; }

        private static string BuildMessage(IReadOnlyList<UnitOfWorkCleanupFailure> failures)
        {
            var phases = failures.Count == 0
                ? "<none>"
                : string.Join(", ", failures.Select(failure => failure.Phase));
            return $"The unit of work released with cleanup failures in phases: {phases}. " +
                   "The primary business exception (if any) is preserved separately.";
        }
    }

    /// <summary>清理失败条目：阶段 + 原始异常。</summary>
    public sealed class UnitOfWorkCleanupFailure
    {
        public UnitOfWorkCleanupFailure(string phase, Exception exception)
        {
            Phase = phase;
            Exception = exception;
        }

        /// <summary>阶段标识（abandon/restore/token-dispose/unit-of-work-dispose/owned-scope-dispose）。</summary>
        public string Phase { get; }

        /// <summary>该阶段的原始异常。</summary>
        public Exception Exception { get; }
    }
}
