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
    public class EfCoreUnitOfWork : UnitOfWorkWithEvents, IUnitOfWorkAbandonable, IUnitOfWorkCommittedNotifier, IUnitOfWorkManagedResource
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
        /// 提交派发期失败/响应丢失由内核判定为「提交结果未知」；
        /// 提交**已确认**后的句柄清理失败作为次级诊断保留（<see cref="UnitOfWorkCommitCleanupFailureException"/>），
        /// 不得改写为提交结果未知。
        /// </summary>
        public override async Task CommitTransactionAsync(CancellationToken cancellationToken = default)
        {
            if (_currentTransaction == null)
            {
                throw new InvalidOperationException("No transaction has been started");
            }

            var committed = false;
            try
            {
                await _currentTransaction.CommitAsync(cancellationToken);
                committed = true;
            }
            finally
            {
                if (committed)
                {
                    try
                    {
                        DisposeTransaction();
                    }
                    catch (Exception cleanupException)
                    {
                        throw new UnitOfWorkCommitCleanupFailureException(
                            "The database commit was confirmed, but releasing the transaction handle failed. " +
                            "The committed fact is preserved; treat this as a cleanup failure, not an unknown commit outcome.",
                            cleanupException);
                    }
                }
                else
                {
                    try
                    {
                        DisposeTransaction();
                    }
                    catch (Exception)
                    {
                        // 提交自身失败：保留根因，清理失败不再叠加。
                    }
                }
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

        /// <summary>受管资源声明种类（逻辑键：Context 声明类型全名）。</summary>
        public string? ManagedResourceKind => _dbContext.GetType().FullName;

        /// <summary>受管资源连接配置（逻辑键；非关系型 Provider 为 null）。</summary>
        public string? ManagedResourceConnectionString => TryReadManagedConnectionString();

        /// <summary>受管资源原生上下文（身份关联；生命周期仍由内核掌握）。</summary>
        public object? ManagedResourceContext => _dbContext;

        /// <summary>当前受管事务句柄（事务开始成功后非 null；仅用于身份比对）。</summary>
        public object? ManagedTransactionHandle => _currentTransaction;

        private string? TryReadManagedConnectionString()
        {
            try
            {
                return _dbContext.Database.GetConnectionString();
            }
            catch (Exception)
            {
                // 非关系型/未配置 Provider：没有连接配置（逻辑键退化为声明种类）。
                return null;
            }
        }

        /// <summary>
        /// 未完成退出的终结：**尽力**回滚并释放事务句柄、丢弃未 flush 的跟踪写入（已 flush 的写入不在丢弃范围；
        /// 跨 UoW 不残留跟踪状态）；终结失败不得吞掉——回滚无法确认时抛
        /// <see cref="UnitOfWorkAbandonFailureException"/>（保留原始故障与后续清理失败），
        /// 回滚已确认但释放/丢弃失败时抛 <see cref="UnitOfWorkAbandonCleanupFailureException"/>；
        /// 可重复调用。
        /// </summary>
        public void AbandonPendingWork()
        {
            Exception? terminationFailure = null;
            var subsequentFailures = new List<Exception>();

            try
            {
                _currentTransaction?.Rollback();
            }
            catch (Exception exception)
            {
                terminationFailure = exception;
            }

            try
            {
                DisposeTransaction();
            }
            catch (Exception exception)
            {
                subsequentFailures.Add(exception);
            }

            try
            {
                _dbContext.ChangeTracker.Clear();
            }
            catch (Exception exception)
            {
                subsequentFailures.Add(exception);
            }

            if (terminationFailure is not null)
            {
                throw new UnitOfWorkAbandonFailureException(
                    "The unit of work could not confirm the rollback of its pending transaction. " +
                    "The original termination failure is preserved; the transaction outcome must not be reported as rolled back.",
                    terminationFailure,
                    subsequentFailures);
            }

            if (subsequentFailures.Count > 0)
            {
                throw new UnitOfWorkAbandonCleanupFailureException(
                    "The pending transaction was rolled back, but releasing the handle or discarding tracked writes failed. " +
                    "The rollback fact is preserved; treat this as a cleanup failure.",
                    subsequentFailures);
            }
        }

        public override void Dispose()
        {
            DisposeTransaction();
        }
    }
}
