using System;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Domain.UnitOfWork;

namespace CrestCreates.Data.Abstractions
{
    /// <summary>
    /// 结算阶段：完成（flush → commit → 提交后通知）、显式/异常回滚与未提交终结。
    /// 同一数据库事实不得因入口不同而变化：所有退出路径共用 <see cref="ClassifyTerminationResult"/>。
    /// </summary>
    internal static class UnitOfWorkScopeSettlement
    {
        internal static async Task CompleteAsync(UnitOfWorkScope scope, CancellationToken cancellationToken)
        {
            scope.ThrowIfReleased();
            if (scope.State == UnitOfWorkState.Completed)
            {
                return; // 重复成功完成 = 幂等 no-op
            }

            if (scope.State != UnitOfWorkState.Active)
            {
                throw new InvalidOperationException($"Cannot complete a unit-of-work scope in state '{scope.State}'.");
            }

            if (!scope.HasStarted)
            {
                throw new InvalidOperationException("The unit of work has not been started. Call StartAsync before completing.");
            }

            if (!scope.IsOwner)
            {
                // join：成功记录前执行同等的截止时间/取消检查（联动 token 生成不等于完成阶段已观察取消）。
                ThrowIfExecutionInterrupted(scope, cancellationToken);

                // join：记录参与者成功；不 flush、不 commit、不释放外层。
                scope.TransitionTo(UnitOfWorkState.Completed);
                return;
            }

            if (scope.RollbackOnly)
            {
                var terminationFailure = await RollbackOwnerCoreAsync(scope).ConfigureAwait(false);
                var rollbackOnly = new UnitOfWorkRollbackOnlyException(
                    "The unit of work was marked rollback-only because an inner participant failed or exited " +
                    "without completing. The transaction was rolled back instead of committing a partial failure.");
                if (terminationFailure is not null)
                {
                    UnitOfWorkDiagnostics.Attach(
                        rollbackOnly,
                        UnitOfWorkManager.RollbackTerminationFailureDataKey,
                        terminationFailure);
                }
                throw rollbackOnly;
            }

            ThrowIfExecutionInterrupted(scope, cancellationToken);

            // 阶段 1：flush（提交前；rollback-only 校验已在此之前完成）。
            try
            {
                await scope.UnitOfWork.SaveChangesAsync(scope.ExecutionToken).ConfigureAwait(false);
            }
            catch (Exception flushFailure)
            {
                var terminationFailure = await TryTerminateUncommittedAsync(scope).ConfigureAwait(false);
                if (terminationFailure is not null)
                {
                    UnitOfWorkDiagnostics.Attach(
                        flushFailure,
                        UnitOfWorkManager.RollbackTerminationFailureDataKey,
                        terminationFailure);
                }

                // 统一分类：已确认回滚（含仅清理失败）保持 RolledBack，无法确认才 Unknown。
                ClassifyTerminationResult(scope, terminationFailure);
                throw;
            }

            // 阶段 2：数据库 commit（确认后立即记录 Committed）。
            if (scope.IsTransactional)
            {
                try
                {
                    await scope.UnitOfWork.CommitTransactionAsync(scope.ExecutionToken).ConfigureAwait(false);
                }
                catch (UnitOfWorkCommitCleanupFailureException)
                {
                    // 提交已确认，句柄清理失败：固定 Committed，记录清理未完成，不尝试回滚。
                    scope.TransactionOutcome = UnitOfWorkTransactionOutcome.Committed;
                    scope.TransitionTo(UnitOfWorkState.Failed);
                    throw;
                }
                catch (Exception commitFailure)
                {
                    // 提交派发期失败/响应丢失：结果未知；回滚尝试不得改写 Unknown。
                    scope.TransactionOutcome = UnitOfWorkTransactionOutcome.Unknown;
                    var terminationFailure = await TryTerminateUncommittedAsync(scope).ConfigureAwait(false);
                    if (terminationFailure is not null)
                    {
                        UnitOfWorkDiagnostics.Attach(
                            commitFailure,
                            UnitOfWorkManager.RollbackTerminationFailureDataKey,
                            terminationFailure);
                    }
                    scope.TransitionTo(UnitOfWorkState.Failed);
                    throw;
                }
            }

            scope.TransactionOutcome = UnitOfWorkTransactionOutcome.Committed;
            scope.TransitionTo(UnitOfWorkState.Completed);

            // 阶段 3：提交后通知（失败不回滚、不谎称、不自动重试）。
            await UnitOfWorkScopeNotification.PublishAsync(scope).ConfigureAwait(false);
        }

        internal static async Task RollbackAsync(UnitOfWorkScope scope, CancellationToken cancellationToken = default)
        {
            scope.ThrowIfReleased();

            if (!scope.IsOwner)
            {
                if (scope.State != UnitOfWorkState.Completed)
                {
                    scope.OwnerScope?.MarkRollbackOnly();
                }
                return;
            }

            if (scope.State == UnitOfWorkState.Completed)
            {
                throw new InvalidOperationException(
                    "Cannot roll back after commit. The unit of work is already committed; the committed fact is preserved.");
            }

            if (scope.State is UnitOfWorkState.RolledBack or UnitOfWorkState.Failed)
            {
                return; // 幂等
            }

            await RollbackOwnerCoreAsync(scope).ConfigureAwait(false);
        }

        /// <summary>异常路径使用：不抛次级异常，保留原始业务异常；返回原始终结故障（无失败为 null）。</summary>
        internal static async Task<Exception?> TryRollbackOnFailureAsync(UnitOfWorkScope scope)
        {
            if (scope.IsReleased)
            {
                return null;
            }

            try
            {
                if (!scope.IsOwner)
                {
                    if (scope.State != UnitOfWorkState.Completed)
                    {
                        scope.OwnerScope?.MarkRollbackOnly();
                    }
                    return null;
                }

                if (scope.State is UnitOfWorkState.Completed or UnitOfWorkState.RolledBack or UnitOfWorkState.Failed)
                {
                    return null;
                }

                return await RollbackOwnerCoreAsync(scope).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                // 防御：次级失败不替换原始业务异常，但保留为可检查结果。
                scope.RollbackTerminationFailure ??= exception;
                return exception;
            }
        }

        /// <summary>
        /// owner 回滚：终结未提交工作并按统一分类迁移结果。返回原始终结故障（无法确认回滚时非 null）。
        /// 终结失败不得宣称 RolledBack：无法确认转 Unknown + Failed，回滚已确认但释放/丢弃失败保持
        /// RolledBack + Failed（详见 <see cref="ClassifyTerminationResult"/>）。
        /// </summary>
        private static async Task<Exception?> RollbackOwnerCoreAsync(UnitOfWorkScope scope)
        {
            if (!scope.HasStarted)
            {
                // 未开始（无事务句柄）：无需回滚。
                scope.TransitionTo(UnitOfWorkState.RolledBack);
                return null;
            }

            var terminationFailure = await TryTerminateUncommittedAsync(scope).ConfigureAwait(false);
            ClassifyTerminationResult(scope, terminationFailure);
            return terminationFailure;
        }

        /// <summary>
        /// 终结结果统一分类（所有退出路径共用；同一数据库事实不得因入口不同而变化）：
        /// 1) 成功回滚：保持/恢复 RolledBack（已有 Unknown 提交结果不得被改写）；
        /// 2) 回滚**已确认**但后续句柄释放/未 flush 丢弃失败（<see cref="UnitOfWorkAbandonCleanupFailureException"/>）：
        ///    事务事实保持 RolledBack，状态 Failed（清理未完成）；
        /// 3) 回滚**无法确认**（其它终结故障）：Unknown + Failed。
        /// 原始终结故障经 scope 的 <c>RollbackTerminationFailure</c> 保留；异常路径由调用方附加到主异常。
        /// </summary>
        internal static void ClassifyTerminationResult(UnitOfWorkScope scope, Exception? terminationFailure)
        {
            if (terminationFailure is null)
            {
                if (scope.TransactionOutcome != UnitOfWorkTransactionOutcome.Unknown)
                {
                    scope.TransactionOutcome = UnitOfWorkTransactionOutcome.RolledBack;
                }

                scope.TransitionTo(UnitOfWorkState.RolledBack);
                return;
            }

            scope.RollbackTerminationFailure ??= terminationFailure;

            if (terminationFailure is UnitOfWorkAbandonCleanupFailureException or
                UnitOfWorkRollbackCleanupFailureException)
            {
                if (scope.TransactionOutcome != UnitOfWorkTransactionOutcome.Unknown)
                {
                    scope.TransactionOutcome = UnitOfWorkTransactionOutcome.RolledBack;
                }
            }
            else if (scope.IsTransactional && scope.TransactionOutcome != UnitOfWorkTransactionOutcome.Unknown)
            {
                scope.TransactionOutcome = UnitOfWorkTransactionOutcome.Unknown;
            }

            scope.TransitionTo(UnitOfWorkState.Failed);
        }

        /// <summary>
        /// 未提交退出的统一终结：优先 <see cref="IUnitOfWorkAbandonable"/>（终结事务 + 丢弃未 flush 跟踪写入），
        /// 否则回滚事务。所有回滚路径（显式回滚/业务异常/flush 失败/提交失败清理）共用，
        /// 避免已回滚的跟踪写入被下一个 UoW 重新持久化。
        /// 返回终结失败（无法确认回滚或释放失败）的原始异常；成功为 null —— Provider 不得吞掉终结故障。
        /// </summary>
        private static async Task<Exception?> TryTerminateUncommittedAsync(UnitOfWorkScope scope)
        {
            try
            {
                if (scope.UnitOfWork is IUnitOfWorkAbandonable abandonable)
                {
                    abandonable.AbandonPendingWork();
                    return null;
                }

                await scope.UnitOfWork.RollbackTransactionAsync(CancellationToken.None).ConfigureAwait(false);
                return null;
            }
            catch (Exception exception)
            {
                // 终结失败结果交回调用方（状态/结果迁移 + 故障保留），不静默吞掉。
                return exception;
            }
        }

        private static void ThrowIfExecutionInterrupted(UnitOfWorkScope scope, CancellationToken cancellationToken)
        {
            if (scope.DeadlineUtc is { } deadline && DateTimeOffset.UtcNow >= deadline)
            {
                throw new OperationCanceledException(
                    "The unit of work deadline expired before completion; the transaction must not be reported as success.");
            }

            if (scope.ExecutionToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(scope.ExecutionToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
        }
    }
}
