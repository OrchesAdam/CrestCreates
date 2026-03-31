using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using CrestCreates.Domain.DomainEvents;
using CrestCreates.Domain.Entities;
using CrestCreates.OrmProviders.Abstract.UnitOfWorkBase;

namespace CrestCreates.OrmProviders.EFCore.UnitOfWork
{
    public class EfCoreUnitOfWork : UnitOfWorkWithEvents
    {
        private readonly DbContext _dbContext;
        private IDbContextTransaction? _currentTransaction;

        public EfCoreUnitOfWork(DbContext dbContext, IDomainEventPublisher domainEventPublisher) 
            : base(domainEventPublisher)
        {
            _dbContext = dbContext;
        }

        public override async Task BeginTransactionAsync()
        {
            if (_currentTransaction != null)
            {
                throw new InvalidOperationException("Transaction already in progress");
            }

            _currentTransaction = await _dbContext.Database.BeginTransactionAsync();
        }

        public override async Task CommitTransactionAsync()
        {
            try
            {
                var entities = GetEntitiesWithDomainEvents();
                await SaveChangesAsync();

                if (_currentTransaction != null)
                {
                    await _currentTransaction.CommitAsync();
                    DisposeTransaction();
                }

                await PublishDomainEventsAsync(entities);
            }
            catch
            {
                await RollbackTransactionAsync();
                throw;
            }
        }

        public override async Task RollbackTransactionAsync()
        {
            try
            {
                if (_currentTransaction != null)
                {
                    await _currentTransaction.RollbackAsync();
                }
            }
            finally
            {
                DisposeTransaction();
            }
        }

        public override async Task<int> SaveChangesAsync()
        {
            return await _dbContext.SaveChangesAsync();
        }

        public async Task<int> SaveChangesWithEventsAsync(CancellationToken cancellationToken = default)
        {
            var entities = GetEntitiesWithDomainEvents();
            var result = await SaveChangesAsync();
            await PublishDomainEventsAsync(entities, cancellationToken);
            return result;
        }

        private List<object> GetEntitiesWithDomainEvents()
        {
            var entities = new List<object>();
            
            foreach (var entry in _dbContext.ChangeTracker.Entries())
            {
                var entityType = entry.Entity.GetType();
                var domainEventsProperty = entityType.GetProperty("DomainEvents");
                var clearDomainEventsMethod = entityType.GetMethod("ClearDomainEvents");
                
                if (domainEventsProperty != null && clearDomainEventsMethod != null)
                {
                    var domainEvents = domainEventsProperty.GetValue(entry.Entity) as System.Collections.Generic.IReadOnlyCollection<IDomainEvent>;
                    if (domainEvents != null && domainEvents.Count > 0)
                    {
                        entities.Add(entry.Entity);
                    }
                }
            }
            
            return entities;
        }

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
                            await PublishWithRetryAsync(domainEvent, cancellationToken);
                        }
                        clearDomainEventsMethod.Invoke(entity, null);
                    }
                }
            }
        }

        private async Task PublishWithRetryAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default, int maxRetries = 3)
        {
            int retryCount = 0;
            while (true)
            {
                try
                {
                    await _domainEventPublisher.PublishAsync(domainEvent, cancellationToken);
                    break;
                }
                catch (Exception ex)
                {
                    retryCount++;
                    if (retryCount >= maxRetries)
                    {
                        // 记录错误但不影响事务
                        // 实际应用中应该使用日志系统
                        Console.WriteLine($"Failed to publish event after {maxRetries} retries: {ex.Message}");
                        break;
                    }
                    
                    // 指数退避
                    await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, retryCount)), cancellationToken);
                }
            }
        }

        private void DisposeTransaction()
        {
            if (_currentTransaction != null)
            {
                _currentTransaction.Dispose();
                _currentTransaction = null;
            }
        }

        public override void Dispose()
        {
            DisposeTransaction();
        }
    }
}
