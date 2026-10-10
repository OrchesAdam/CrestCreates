using System;
using System.Collections.Generic;
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
    public class FreeSqlUnitOfWork : UnitOfWorkWithEvents, IUnitOfWorkCommittedNotifier
    {
        private readonly FreeSqlUnitOfWorkManager _unitOfWorkManager;
        private global::FreeSql.IUnitOfWork? _unitOfWork;
        private bool _disposed;
        private readonly List<object> _trackedEntities = new List<object>();

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

            // 仓储从同一个 FreeSqlUnitOfWorkManager.Orm 获取连接，确保事务边界一致。
            _unitOfWork = _unitOfWorkManager.Begin();
            return Task.CompletedTask;
        }

        /// <summary>
        /// 数据库提交（不含 flush 与通知）。
        /// </summary>
        public override async Task CommitTransactionAsync(CancellationToken cancellationToken = default)
        {
            if (_unitOfWork == null)
            {
                throw new InvalidOperationException("No transaction has been started");
            }

            try
            {
                await Task.Run(() => _unitOfWork.Commit(), cancellationToken);
            }
            finally
            {
                DisposeUnitOfWork();
            }
        }

        /// <summary>
        /// 回滚事务
        /// </summary>
        public override Task RollbackTransactionAsync(CancellationToken cancellationToken = default)
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
            foreach (var entity in new List<object>(_trackedEntities))
            {
                var entityType = entity.GetType();
                var domainEventsProperty = entityType.GetProperty("DomainEvents");
                var clearDomainEventsMethod = entityType.GetMethod("ClearDomainEvents");

                if (domainEventsProperty == null || clearDomainEventsMethod == null)
                {
                    continue;
                }

                if (domainEventsProperty.GetValue(entity) is IReadOnlyCollection<IDomainEvent> domainEvents)
                {
                    foreach (var domainEvent in domainEvents)
                    {
                        await _domainEventPublisher.PublishAsync(domainEvent, cancellationToken);
                    }

                    clearDomainEventsMethod.Invoke(entity, null);
                }
            }

            _trackedEntities.Clear();
        }

        /// <summary>
        /// 跟踪实体以发布领域事件
        /// </summary>
        public void TrackEntity<TEntity, TId>(TEntity entity)
            where TEntity : Entity<TId>
            where TId : IEquatable<TId>
        {
            if (entity != null && entity.DomainEvents.Count > 0)
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
                // 如果事务还在进行中，自动回滚
                if (_unitOfWork != null)
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
