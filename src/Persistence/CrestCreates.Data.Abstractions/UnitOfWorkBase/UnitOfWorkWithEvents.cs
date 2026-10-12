using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Domain.DomainEvents;
using CrestCreates.Domain.Entities;
using CrestCreates.Domain.UnitOfWork;

namespace CrestCreates.Data.Abstractions.UnitOfWorkBase
{
    public abstract class UnitOfWorkWithEvents : IUnitOfWork
    {
        protected readonly IDomainEventPublisher _domainEventPublisher;

        protected UnitOfWorkWithEvents(IDomainEventPublisher domainEventPublisher)
        {
            _domainEventPublisher = domainEventPublisher;
        }

        public abstract Task BeginTransactionAsync(UnitOfWorkBeginOptions options, CancellationToken cancellationToken = default);
        public abstract Task CommitTransactionAsync(CancellationToken cancellationToken = default);
        public abstract Task RollbackTransactionAsync(CancellationToken cancellationToken = default);
        public abstract Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
        public abstract void Dispose();

        protected async Task PublishDomainEventsAsync<TId>(IEnumerable<Entity<TId>> entities, CancellationToken cancellationToken = default) where TId : IEquatable<TId>
        {
            foreach (var entity in entities)
            {
                foreach (var domainEvent in entity.DomainEvents)
                {
                    await _domainEventPublisher.PublishAsync(domainEvent, cancellationToken);
                }
                entity.ClearDomainEvents();
            }
        }

        protected async Task<int> SaveChangesWithEventsAsync<TEntity, TId>(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default) where TEntity : Entity<TId> where TId : IEquatable<TId>
        {
            var result = await SaveChangesAsync(cancellationToken);
            await PublishDomainEventsAsync(entities, cancellationToken);
            return result;
        }
    }
}
