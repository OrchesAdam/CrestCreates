using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.DbContextProvider.Abstract;
using CrestCreates.Domain.DomainEvents;
using CrestCreates.Domain.UnitOfWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using CrestCreates.Data.Abstractions;
using CrestCreates.Data.Abstractions.UnitOfWorkBase;
using Microsoft.Extensions.DependencyInjection;

namespace CrestCreates.Data.EFCore.UnitOfWork
{
    /// <summary>
    /// EF Core 工作单元：flush / commit / 提交后通知职责分离（内核负责顺序）。
    /// </summary>
    /// <remarks>
    /// <c>CommitTransactionAsync</c> 只做数据库提交并释放句柄；flush（SaveChanges）与
    /// 提交后通知（<see cref="IUnitOfWorkCommittedNotifier"/>）由内核按
    /// 「校验→flush→commit（确认即记录）→通知→清理」调用。
    /// </remarks>
    public class EfCoreUnitOfWork : UnitOfWorkWithEvents, IUnitOfWorkTransactionAbortable, IUnitOfWorkCommittedNotifier
    {
        private readonly DbContext _dbContext;
        private IDbContextTransaction? _currentTransaction;

        [ActivatorUtilitiesConstructor]
        public EfCoreUnitOfWork(IDataBaseContext dbContext, IDomainEventPublisher domainEventPublisher)
            : this(GetDbContext(dbContext), domainEventPublisher)
        {
        }

        public EfCoreUnitOfWork(DbContext dbContext, IDomainEventPublisher domainEventPublisher)
            : base(domainEventPublisher)
        {
            _dbContext = dbContext;
        }

        private static DbContext GetDbContext(IDataBaseContext dbContext)
        {
            return dbContext.GetNativeContext() as DbContext
                ?? throw new InvalidOperationException(
                    $"The configured {nameof(IDataBaseContext)} does not wrap an Entity Framework Core {nameof(DbContext)}.");
        }

        public override async Task BeginTransactionAsync(
            UnitOfWorkBeginOptions options,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(options);

            if (_currentTransaction != null)
            {
                throw new InvalidOperationException("Transaction already in progress");
            }

            _currentTransaction = options.IsolationLevel is { } isolationLevel
                ? await _dbContext.Database.BeginTransactionAsync(isolationLevel, cancellationToken)
                : await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        }

        /// <summary>
        /// 数据库提交（不含 flush 与通知）；句柄在成功或失败后都恰好释放一次。
        /// 提交派发期失败/响应丢失由内核判定为「提交结果未知」。
        /// </summary>
        public override async Task CommitTransactionAsync(CancellationToken cancellationToken = default)
        {
            if (_currentTransaction == null)
            {
                throw new InvalidOperationException("No transaction has been started");
            }

            try
            {
                await _currentTransaction.CommitAsync(cancellationToken);
            }
            finally
            {
                DisposeTransaction();
            }
        }

        public override async Task RollbackTransactionAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                if (_currentTransaction != null)
                {
                    await _currentTransaction.RollbackAsync(cancellationToken);
                }
            }
            finally
            {
                DisposeTransaction();
            }
        }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            return _dbContext.SaveChangesAsync(cancellationToken);
        }

        public async Task<int> SaveChangesWithEventsAsync(CancellationToken cancellationToken = default)
        {
            var result = await SaveChangesAsync(cancellationToken);
            await PublishCommittedNotificationsAsync(cancellationToken);
            return result;
        }

        /// <summary>
        /// 提交后通知：发布待发域事件。失败不吞掉、不清空未成功发布的实体事件队列（清空条件 = 发布成功）。
        /// </summary>
        public async Task PublishCommittedNotificationsAsync(CancellationToken cancellationToken = default)
        {
            foreach (var entity in GetEntitiesWithDomainEvents())
            {
                foreach (var domainEvent in entity.DomainEvents.ToArray())
                {
                    await _domainEventPublisher.PublishAsync(domainEvent, cancellationToken);
                }

                entity.ClearDomainEvents();
            }
        }

        private List<IHasDomainEvents> GetEntitiesWithDomainEvents()
        {
            var entities = new List<IHasDomainEvents>();

            foreach (var entry in _dbContext.ChangeTracker.Entries<IHasDomainEvents>())
            {
                if (entry.Entity.DomainEvents.Count > 0)
                {
                    entities.Add(entry.Entity);
                }
            }

            return entities;
        }

        private void DisposeTransaction()
        {
            if (_currentTransaction != null)
            {
                _currentTransaction.Dispose();
                _currentTransaction = null;
            }
        }

        /// <summary>
        /// 逻辑工作单元的事务终结：scope 未完成退出时回滚并释放未提交事务，
        /// 只终结事务句柄，不释放 DI 持有的上下文对象；可重复调用。
        /// </summary>
        public void AbortPendingTransaction()
        {
            try
            {
                _currentTransaction?.Rollback();
            }
            catch (Exception)
            {
                // Best effort: the handle is released below regardless, and the
                // failure that triggered scope unwinding must not be replaced.
            }
            finally
            {
                DisposeTransaction();
            }
        }

        public override void Dispose()
        {
            DisposeTransaction();
        }
    }
}
