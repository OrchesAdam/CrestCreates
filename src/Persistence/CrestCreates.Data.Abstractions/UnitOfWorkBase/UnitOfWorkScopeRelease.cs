using System;
using System.Collections.Generic;
using CrestCreates.Domain.UnitOfWork;

namespace CrestCreates.Data.Abstractions
{
    /// <summary>
    /// 释放阶段：未完成退出先终结（owner）或标记 rollback-only（join），再恢复调用方环境并
    /// 恰好释放一次资源。清理失败聚合为 <see cref="UnitOfWorkCleanupException"/>。
    /// </summary>
    internal static class UnitOfWorkScopeRelease
    {
        internal static void Dispose(UnitOfWorkScope scope)
        {
            if (scope.IsReleased)
            {
                return;
            }

            var failures = new List<UnitOfWorkCleanupFailure>();

            // owner 未完成退出：回滚 + 丢弃 + 释放（不等请求 DI scope 结束）。
            if (scope.IsOwner && scope.State == UnitOfWorkState.Active)
            {
                TryAbandonOwner(scope, failures);
            }
            else if (!scope.IsOwner && scope.State == UnitOfWorkState.Active)
            {
                // 未成功完成的 join 退出：标记实际 owner rollback-only；
                // 成功完成的 join 仅恢复环境（不污染外层）。
                scope.OwnerScope?.MarkRollbackOnly();
            }

            scope.Manager.RestoreExecutionParent(scope);

            try
            {
                scope.Manager.RestoreCallerEnvironment(scope);
            }
            catch (Exception exception)
            {
                failures.Add(new UnitOfWorkCleanupFailure("restore", exception));
            }

            try
            {
                scope.ExecutionCts?.Dispose();
            }
            catch (Exception exception)
            {
                failures.Add(new UnitOfWorkCleanupFailure("token-dispose", exception));
            }

            if (scope.OwnsUnitOfWorkDirectly)
            {
                try
                {
                    scope.UnitOfWork.Dispose();
                }
                catch (Exception exception)
                {
                    failures.Add(new UnitOfWorkCleanupFailure("unit-of-work-dispose", exception));
                }
            }

            try
            {
                scope.OwnedScope?.Dispose();
            }
            catch (Exception exception)
            {
                failures.Add(new UnitOfWorkCleanupFailure("owned-scope-dispose", exception));
            }

            scope.MarkReleased();

            if (failures.Count > 0)
            {
                throw new UnitOfWorkCleanupException(failures);
            }
        }

        private static void TryAbandonOwner(UnitOfWorkScope scope, List<UnitOfWorkCleanupFailure> failures)
        {
            if (scope.UnitOfWork is IUnitOfWorkAbandonable abandonable)
            {
                try
                {
                    // DI 路径：容器负责对象释放，内核终结未完成事务并丢弃未 flush 跟踪写入。
                    abandonable.AbandonPendingWork();
                }
                catch (Exception exception)
                {
                    failures.Add(new UnitOfWorkCleanupFailure("abandon", exception));

                    // 与其它退出路径共用同一分类：已确认回滚（含仅清理失败）保持 RolledBack，
                    // 无法确认才 Unknown；原始终结故障同时保留在 scope 诊断上。
                    UnitOfWorkScopeSettlement.ClassifyTerminationResult(scope, exception);
                    return;
                }
            }

            // 无终结能力的 Provider：依赖其容器释放语义（见 Provider 矩阵）。
            scope.TransactionOutcome = scope.IsTransactional
                ? UnitOfWorkTransactionOutcome.RolledBack
                : UnitOfWorkTransactionOutcome.NotStarted;
            scope.TransitionTo(UnitOfWorkState.RolledBack);
        }
    }
}
