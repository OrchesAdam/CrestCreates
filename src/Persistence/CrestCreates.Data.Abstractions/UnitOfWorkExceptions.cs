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

