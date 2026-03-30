using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Domain.DomainEvents;
using CrestCreates.Domain.Entities;
using CrestCreates.Domain.UnitOfWork;

namespace CrestCreates.OrmProviders.Abstract.UnitOfWorkBase
{
    public abstract class UnitOfWorkWithEvents : IUnitOfWork
    {
        private readonly IDomainEventPublisher _domainEventPublisher;

        protected UnitOfWorkWithEvents(IDomainEventPublisher domainEventPublisher)
        {
            _domainEventPublisher = domainEventPublisher;
        }

        public abstract Task BeginTransactionAsync();
        public abstract Task CommitTransactionAsync();
        public abstract Task RollbackTransactionAsync();
        public abstract Task<int> SaveChangesAsync();
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
            var result = await SaveChangesAsync();
            await PublishDomainEventsAsync(entities, cancellationToken);
            return result;
        }
    }
}
