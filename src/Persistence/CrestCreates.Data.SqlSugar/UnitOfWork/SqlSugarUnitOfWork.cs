using System;
using System.Collections.Generic;
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
    public class SqlSugarUnitOfWork : UnitOfWorkWithEvents, IUnitOfWorkCommittedNotifier
    {
        private readonly ISqlSugarClient _sqlSugarClient;
        private bool _isTransactionStarted;
        private bool _disposed;
        private readonly List<object> _trackedEntities = new List<object>();

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

            await Task.Run(() => _sqlSugarClient.Ado.BeginTran(), cancellationToken);
            _isTransactionStarted = true;
        }

        /// <summary>
        /// 数据库提交（不含 flush 与通知）。
        /// </summary>
        public override async Task CommitTransactionAsync(CancellationToken cancellationToken = default)
        {
            if (!_isTransactionStarted)
            {
                throw new InvalidOperationException("No transaction has been started");
            }

            try
            {
                await Task.Run(() => _sqlSugarClient.Ado.CommitTran(), cancellationToken);
            }
            finally
            {
                _isTransactionStarted = false;
            }
        }

        /// <summary>
        /// 回滚事务
        /// </summary>
        public override async Task RollbackTransactionAsync(CancellationToken cancellationToken = default)
        {
            if (!_isTransactionStarted)
            {
                return;
            }

            try
            {
                await Task.Run(() => _sqlSugarClient.Ado.RollbackTran(), CancellationToken.None);
            }
            finally
            {
                _isTransactionStarted = false;
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
                if (_isTransactionStarted)
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
