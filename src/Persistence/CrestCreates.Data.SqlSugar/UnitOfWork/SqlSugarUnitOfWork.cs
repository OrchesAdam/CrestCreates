using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using SqlSugar;
using CrestCreates.Domain.UnitOfWork;
using CrestCreates.Domain.DomainEvents;
using CrestCreates.Domain.Entities;
using CrestCreates.Data.Abstractions;
using CrestCreates.Data.Abstractions.UnitOfWorkBase;

namespace CrestCreates.Data.SqlSugar.UnitOfWork
{
    /// <summary>
    /// SqlSugar 工作单元实现：flush / commit / 提交后通知职责分离（内核负责顺序）。
    /// </summary>
    public class SqlSugarUnitOfWork : UnitOfWorkWithEvents, IUnitOfWorkCommittedNotifier, IUnitOfWorkAbandonable, IUnitOfWorkDomainEventTracker
    {
        private readonly ISqlSugarClient _sqlSugarClient;
        private bool _isTransactionStarted;
        private bool _disposed;
        private int _operationInProgress;
        private readonly List<IHasDomainEvents> _trackedEntities = new List<IHasDomainEvents>();

        public SqlSugarUnitOfWork(ISqlSugarClient sqlSugarClient, IDomainEventPublisher domainEventPublisher)
            : base(domainEventPublisher)
        {
            _sqlSugarClient = sqlSugarClient ?? throw new ArgumentNullException(nameof(sqlSugarClient));
        }

        /// <summary>
        /// 开始事务（显式隔离级别未声明支持，执行前拒绝，不静默降级）。
        /// </summary>
        public override async Task BeginTransactionAsync(
            UnitOfWorkBeginOptions options,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(options);

            if (options.IsolationLevel is not null)
            {
                throw new NotSupportedException(
                    "The SqlSugar unit of work does not declare support for explicit isolation levels. " +
                    "Do not request an isolation level for this provider.");
            }

            if (_isTransactionStarted)
            {
                throw new InvalidOperationException("Transaction already in progress");
            }

            EnterOperation("begin");
            try
            {
                await Task.Run(() => _sqlSugarClient.Ado.BeginTran(), cancellationToken);
                _isTransactionStarted = true;
            }
            finally
            {
                ExitOperation();
            }
        }

        /// <summary>
        /// 数据库提交（不含 flush 与通知）。
        /// </summary>
        public override async Task CommitTransactionAsync(CancellationToken cancellationToken = default)
        {
            EnterOperation("commit");
            try
            {
                if (!_isTransactionStarted)
                {
                    throw new InvalidOperationException("No transaction has been started");
                }

                var committed = false;
                try
                {
                    await Task.Run(() => _sqlSugarClient.Ado.CommitTran(), cancellationToken);
                    committed = true;
                }
                finally
                {
                    // 提交未确认时保留事务标记，交由内核 abandon/Dispose 终结；不得提前丢弃所有权。
                    if (committed)
                    {
                        _isTransactionStarted = false;
                    }
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
        public override async Task RollbackTransactionAsync(CancellationToken cancellationToken = default)
        {
            EnterOperation("rollback");
            try
            {
                if (!_isTransactionStarted)
                {
                    return;
                }

                await Task.Run(() => _sqlSugarClient.Ado.RollbackTran(), CancellationToken.None);
                _isTransactionStarted = false;
            }
            finally
            {
                ExitOperation();
            }
        }

        /// <summary>
        /// 保存变更（SqlSugar 立即执行模式：无显式 flush；返回 0 表示成功）。
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

        /// <summary>
        /// 释放资源
        /// </summary>
        public override void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// 未完成退出的终结：未完成事务回滚，清空已跟踪实体的域事件队列；
        /// 终结失败不得吞掉——回滚无法确认时抛 <see cref="UnitOfWorkAbandonFailureException"/>
        /// （原始终结故障保留）；可重复调用。
        /// </summary>
        public void AbandonPendingWork()
        {
            if (!TryEnterOperation())
            {
                throw new UnitOfWorkAbandonFailureException(
                    "The SqlSugar unit of work still has an operation in progress; termination cannot be confirmed. " +
                    "The transaction outcome must not be reported as rolled back.",
                    new InvalidOperationException("A commit or rollback is still executing on the same unit of work."));
            }

            try
            {
                Exception? terminationFailure = null;

                if (_isTransactionStarted)
                {
                    try
                    {
                        _sqlSugarClient.Ado.RollbackTran();
                    }
                    catch (Exception exception)
                    {
                        terminationFailure = exception;
                    }

                    _isTransactionStarted = false;
                }

                _trackedEntities.Clear();

                if (terminationFailure is not null)
                {
                    throw new UnitOfWorkAbandonFailureException(
                        "The SqlSugar unit of work could not confirm the rollback of its pending transaction. " +
                        "The original termination failure is preserved; the transaction outcome must not be reported as rolled back.",
                        terminationFailure);
                }
            }
            finally
            {
                ExitOperation();
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

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            if (disposing)
            {
                // 如果事务还在进行中且没有操作占用，自动回滚；正在执行的操作保留所有权。
                if (_isTransactionStarted && Volatile.Read(ref _operationInProgress) == 0)
                {
                    try
                    {
                        _sqlSugarClient.Ado.RollbackTran();
                    }
                    catch
                    {
                        // 忽略回滚异常
                    }
                    finally
                    {
                        _isTransactionStarted = false;
                    }
                }

                _trackedEntities.Clear();
            }

            _disposed = true;
        }

        ~SqlSugarUnitOfWork()
        {
            Dispose(false);
        }
    }
}
