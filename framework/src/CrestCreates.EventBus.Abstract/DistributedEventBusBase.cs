using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Domain.DomainEvents;

namespace CrestCreates.EventBus.Abstract
{
    public abstract class DistributedEventBusBase : IEventBus
    {
        public abstract Task PublishAsync(IDomainEvent @event, CancellationToken cancellationToken = default);
        
        public abstract Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default) where TEvent : IDomainEvent;
        
        public abstract void Subscribe<TEvent, THandler>() where TEvent : IDomainEvent where THandler : IEventHandler<TEvent>;
        
        public abstract void Unsubscribe<TEvent, THandler>() where TEvent : IDomainEvent where THandler : IEventHandler<TEvent>;
    }
}