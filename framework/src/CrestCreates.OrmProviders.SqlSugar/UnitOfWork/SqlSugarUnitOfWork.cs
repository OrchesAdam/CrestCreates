using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using SqlSugar;
using CrestCreates.Domain.UnitOfWork;
using CrestCreates.Domain.DomainEvents;
using CrestCreates.Domain.Entities;
using CrestCreates.OrmProviders.Abstract.UnitOfWorkBase;
using CrestCreates.Infrastructure.EventBus;

namespace CrestCreates.OrmProviders.SqlSugar.UnitOfWork
{
    /// <summary>
    /// SqlSugar 工作单元实现
    /// 提供事务管理、变更追踪和领域事件发布功能
    /// </summary>
    public class SqlSugarUnitOfWork : UnitOfWorkWithEvents
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
        /// 开始事务
        /// </summary>
        public override async Task BeginTransactionAsync()
        {
            if (_isTransactionStarted)
            {
                throw new InvalidOperationException("Transaction already in progress");
            }

            await Task.Run(() => _sqlSugarClient.Ado.BeginTran());
            _isTransactionStarted = true;
        }

        /// <summary>
        /// 提交事务
        /// </summary>
        public override async Task CommitTransactionAsync()
        {
            if (!_isTransactionStarted)
            {
                throw new InvalidOperationException("No transaction has been started");
            }

            try
            {
                await SaveChangesWithEventsAsync();
                await Task.Run(() => _sqlSugarClient.Ado.CommitTran());
                _isTransactionStarted = false;
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
        public override async Task RollbackTransactionAsync()
        {
            if (!_isTransactionStarted)
            {
                return;
            }

            try
            {
                await Task.Run(() => _sqlSugarClient.Ado.RollbackTran());
            }
            finally
            {
                _isTransactionStarted = false;
            }
        }

        /// <summary>
        /// 保存变更（SqlSugar不需要显式调用SaveChanges）
        /// </summary>
        /// <returns>影响的行数（对于SqlSugar，返回0表示成功）</returns>
        public override Task<int> SaveChangesAsync()
        {
            // SqlSugar 是立即执行模式，不需要显式保存
            // 但为了与接口兼容，返回成功状态
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
