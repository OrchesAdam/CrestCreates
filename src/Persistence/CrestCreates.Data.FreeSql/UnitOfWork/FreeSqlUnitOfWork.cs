using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using CrestCreates.Domain.UnitOfWork;
using CrestCreates.Domain.DomainEvents;
using CrestCreates.Domain.Entities;
using CrestCreates.Data.Abstractions;
using CrestCreates.Data.Abstractions.UnitOfWorkBase;

namespace CrestCreates.Data.FreeSql.UnitOfWork
{
    /// <summary>
    /// FreeSql 工作单元实现：flush / commit / 提交后通知职责分离（内核负责顺序）。
    /// </summary>
    public class FreeSqlUnitOfWork : UnitOfWorkWithEvents, IUnitOfWorkCommittedNotifier, IUnitOfWorkAbandonable, IUnitOfWorkDomainEventTracker
    {
        private readonly FreeSqlUnitOfWorkManager _unitOfWorkManager;
        private global::FreeSql.IUnitOfWork? _unitOfWork;
        private bool _disposed;
        private int _operationInProgress;
        private readonly List<IHasDomainEvents> _trackedEntities = new List<IHasDomainEvents>();

        public FreeSqlUnitOfWork(FreeSqlUnitOfWorkManager unitOfWorkManager, IDomainEventPublisher domainEventPublisher)
            : base(domainEventPublisher)
        {
            _unitOfWorkManager = unitOfWorkManager ?? throw new ArgumentNullException(nameof(unitOfWorkManager));
        }

        /// <summary>
        /// 开始事务（显式隔离级别未声明支持，执行前拒绝，不静默降级）。
        /// </summary>
        public override Task BeginTransactionAsync(
            UnitOfWorkBeginOptions options,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(options);

            if (options.IsolationLevel is not null)
            {
                throw new NotSupportedException(
                    "The FreeSql unit of work does not declare support for explicit isolation levels. " +
                    "Do not request an isolation level for this provider.");
            }

            if (_unitOfWork != null)
            {
                throw new InvalidOperationException("Transaction already in progress");
            }

            EnterOperation("begin");
            try
            {
                // 仓储从同一个 FreeSqlUnitOfWorkManager.Orm 获取连接，确保事务边界一致。
                _unitOfWork = _unitOfWorkManager.Begin();
            }
            finally
            {
                ExitOperation();
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// 数据库提交（不含 flush 与通知）；提交已确认后的句柄清理失败作为次级诊断保留。
        /// </summary>
        public override async Task CommitTransactionAsync(CancellationToken cancellationToken = default)
        {
            EnterOperation("commit");
            try
            {
                if (_unitOfWork == null)
                {
                    throw new InvalidOperationException("No transaction has been started");
                }

                var committed = false;
                try
                {
                    await Task.Run(() => _unitOfWork.Commit(), cancellationToken);
                    committed = true;
                }
                finally
                {
                    if (committed)
                    {
                        try
                        {
                            DisposeUnitOfWork();
                        }
                        catch (Exception cleanupException)
                        {
                            throw new UnitOfWorkCommitCleanupFailureException(
                                "The database commit was confirmed, but releasing the FreeSql unit of work failed. " +
                                "The committed fact is preserved; treat this as a cleanup failure, not an unknown commit outcome.",
                                cleanupException);
                        }
                    }
                    // 提交未确认：保留句柄，交由内核的 abandon/Dispose 终结，避免未确认时丢弃可回滚资源。
                }
            }
            finally
            {
                ExitOperation();
            }
        }

        /// <summary>
        /// 回滚事务
        /// </summary>
        public override Task RollbackTransactionAsync(CancellationToken cancellationToken = default)
        {
            EnterOperation("rollback");
            try
            {
                if (_unitOfWork == null)
                {
                    return Task.CompletedTask;
                }

                try
                {
                    _unitOfWork.Rollback();
                }
                finally
                {
                    DisposeUnitOfWork();
                }
            }
            finally
            {
                ExitOperation();
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// 保存变更（FreeSql 立即执行模式：无显式 flush；返回 0 表示成功）。
        /// </summary>
        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(0);
        }

        /// <summary>
        /// 保存变更并发布领域事件（兼容入口；提交后通知由内核按顺序调用）。
        /// </summary>
        public async Task<int> SaveChangesWithEventsAsync(CancellationToken cancellationToken = default)
        {
            var result = await SaveChangesAsync(cancellationToken);
            await PublishCommittedNotificationsAsync(cancellationToken);
            return result;
        }

        /// <summary>
        /// 提交后通知：发布已跟踪实体的域事件；失败不吞掉、不清空未成功发布的事件队列。
        /// </summary>
        public async Task PublishCommittedNotificationsAsync(CancellationToken cancellationToken = default)
        {
            foreach (var entity in new List<IHasDomainEvents>(_trackedEntities))
            {
                foreach (var domainEvent in entity.DomainEvents.ToArray())
                {
                    await _domainEventPublisher.PublishAsync(domainEvent, cancellationToken);
                }

                entity.ClearDomainEvents();
                _trackedEntities.Remove(entity);
            }
        }

        /// <summary>
        /// 跟踪实体以发布领域事件
        /// </summary>
        public void TrackEntity<TEntity, TId>(TEntity entity)
            where TEntity : Entity<TId>
            where TId : IEquatable<TId>
            => TrackEntity((IHasDomainEvents)entity);

        /// <inheritdoc />
        public void TrackEntity(IHasDomainEvents entity)
        {
            if (entity.DomainEvents.Count == 0)
            {
                return;
            }

            if (!_trackedEntities.Any(tracked => ReferenceEquals(tracked, entity)))
            {
                _trackedEntities.Add(entity);
            }
        }

        private void DisposeUnitOfWork()
        {
            if (_unitOfWork != null)
            {
                _unitOfWork.Dispose();
                _unitOfWork = null;
            }
        }

        private void EnterOperation(string operation)
        {
            if (Interlocked.CompareExchange(ref _operationInProgress, 1, 0) != 0)
            {
                throw new InvalidOperationException(
                    $"A unit-of-work operation is already in progress; '{operation}' was rejected to keep " +
                    "the native transaction under a single owner.");
            }
        }

        private void ExitOperation() => Interlocked.Exchange(ref _operationInProgress, 0);

        private bool TryEnterOperation() => Interlocked.CompareExchange(ref _operationInProgress, 1, 0) == 0;

        /// <summary>
        /// 未完成退出的终结：**尽力**回滚并释放 SDK 工作单元，清空已跟踪实体的域事件队列；
        /// 终结失败不得吞掉——回滚无法确认时抛 <see cref="UnitOfWorkAbandonFailureException"/>，
        /// 回滚已确认但释放失败时抛 <see cref="UnitOfWorkAbandonCleanupFailureException"/>；可重复调用。
        /// </summary>
        public void AbandonPendingWork()
        {
            if (!TryEnterOperation())
            {
                throw new UnitOfWorkAbandonFailureException(
                    "The FreeSql unit of work still has an operation in progress; termination cannot be confirmed. " +
                    "The transaction outcome must not be reported as rolled back.",
                    new InvalidOperationException("A commit or rollback is still executing on the same unit of work."));
            }

            try
            {
                Exception? terminationFailure = null;
                var subsequentFailures = new List<Exception>();

                try
                {
                    _unitOfWork?.Rollback();
                }
                catch (Exception exception)
                {
                    // 回滚未确认：保留句柄与所有权，后续 abandon/Dispose 仍可重试终结。
                    terminationFailure = exception;
                }

                if (_unitOfWork != null && terminationFailure is null)
                {
                    try
                    {
                        DisposeUnitOfWork();
                    }
                    catch (Exception exception)
                    {
                        subsequentFailures.Add(exception);
                    }
                }

                _trackedEntities.Clear();

                if (terminationFailure is not null)
                {
                    throw new UnitOfWorkAbandonFailureException(
                        "The FreeSql unit of work could not confirm the rollback of its pending transaction. " +
                        "The original termination failure is preserved; the transaction outcome must not be reported as rolled back.",
                        terminationFailure,
                        subsequentFailures);
                }

                if (subsequentFailures.Count > 0)
                {
                    throw new UnitOfWorkAbandonCleanupFailureException(
                        "The pending FreeSql transaction was rolled back, but releasing the unit of work failed. " +
                        "The rollback fact is preserved; treat this as a cleanup failure.",
                        subsequentFailures);
                }
            }
            finally
            {
                ExitOperation();
            }
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public override void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            if (disposing)
            {
                // 如果事务还在进行中且没有操作占用，自动回滚；正在执行的操作保留所有权。
                if (_unitOfWork != null && Volatile.Read(ref _operationInProgress) == 0)
                {
                    try
                    {
                        _unitOfWork.Rollback();
                    }
                    catch
                    {
                        // 忽略回滚异常
                    }
                    finally
                    {
                        DisposeUnitOfWork();
                    }
                }

                _trackedEntities.Clear();
            }

            _disposed = true;
        }

        ~FreeSqlUnitOfWork()
        {
            Dispose(false);
        }
    }
}
