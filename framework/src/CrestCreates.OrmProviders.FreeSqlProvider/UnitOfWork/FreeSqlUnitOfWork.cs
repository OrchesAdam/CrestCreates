using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using FreeSql;
using CrestCreates.Domain.UnitOfWork;
using CrestCreates.Domain.DomainEvents;
using CrestCreates.Domain.Entities;
using CrestCreates.OrmProviders.Abstract.UnitOfWorkBase;
using CrestCreates.Infrastructure.EventBus;

namespace CrestCreates.OrmProviders.FreeSqlProvider.UnitOfWork
{
    /// <summary>
    /// FreeSql 工作单元实现
    /// 提供事务管理、变更追踪和领域事件发布功能
    /// </summary>
    public class FreeSqlUnitOfWork : UnitOfWorkWithEvents
    {
        private readonly IFreeSql _freeSql;
        private FreeSql.IUnitOfWork? _unitOfWork;
        private bool _disposed;
        private readonly List<object> _trackedEntities = new List<object>();

        public FreeSqlUnitOfWork(IFreeSql freeSql, IDomainEventPublisher domainEventPublisher) 
            : base(domainEventPublisher)
        {
            _freeSql = freeSql ?? throw new ArgumentNullException(nameof(freeSql));
        }

        /// <summary>
        /// 开始事务
        /// </summary>
        public override Task BeginTransactionAsync()
        {
            if (_unitOfWork != null)
            {
                throw new InvalidOperationException("Transaction already in progress");
            }

            // FreeSql 的 UnitOfWork 会自动开启事务
            _unitOfWork = _freeSql.CreateUnitOfWork();
            return Task.CompletedTask;
        }

        /// <summary>
        /// 提交事务
        /// </summary>
        public override async Task CommitTransactionAsync()
        {
            if (_unitOfWork == null)
            {
                throw new InvalidOperationException("No transaction has been started");
            }

            try
            {
                await SaveChangesWithEventsAsync();
                await Task.Run(() => _unitOfWork.Commit());
                DisposeUnitOfWork();
            }
            catch
            {
                await RollbackTransactionAsync();
                throw;
            }
        }

        /// <summary>
        /// 回滚事务
        /// </summary>
        public override Task RollbackTransactionAsync()
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
        /// 保存变更
        /// </summary>
        /// <returns>影响的行数</returns>
        public override Task<int> SaveChangesAsync()
        {
            // FreeSql 使用 Commit 来保存变更
            // 这里返回0表示成功（实际影响行数在Commit时已处理）
            return Task.FromResult(0);
        }

        /// <summary>
        /// 保存变更并发布领域事件
        /// </summary>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>影响的行数</returns>
        public async Task<int> SaveChangesWithEventsAsync(CancellationToken cancellationToken = default)
        {
            var result = await SaveChangesAsync();
            await PublishDomainEventsAsync(_trackedEntities, cancellationToken);
            _trackedEntities.Clear();
            return result;
        }

        /// <summary>
        /// 跟踪实体以发布领域事件
        /// </summary>
        /// <typeparam name="TEntity">实体类型</typeparam>
        /// <typeparam name="TId">实体ID类型</typeparam>
        /// <param name="entity">要跟踪的实体</param>
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
        /// 发布领域事件
        /// </summary>
        private async Task PublishDomainEventsAsync(List<object> entities, CancellationToken cancellationToken = default)
        {
            foreach (var entity in entities)
            {
                var entityType = entity.GetType();
                var domainEventsProperty = entityType.GetProperty("DomainEvents");
                var clearDomainEventsMethod = entityType.GetMethod("ClearDomainEvents");
                
                if (domainEventsProperty != null && clearDomainEventsMethod != null)
                {
                    var domainEvents = domainEventsProperty.GetValue(entity) as System.Collections.Generic.IReadOnlyCollection<IDomainEvent>;
                    if (domainEvents != null)
                    {
                        foreach (var domainEvent in domainEvents)
                        {
                            await _domainEventPublisher.PublishAsync(domainEvent, cancellationToken);
                        }
                        clearDomainEventsMethod.Invoke(entity, null);
                    }
                }
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
