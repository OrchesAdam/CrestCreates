using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Domain.DomainEvents;
using CrestCreates.Event.Abstractions;

namespace CrestCreates.EventBus.Abstract
{
    public abstract class DistributedEventBusBase : IEventBus
    {
        private readonly IEventValidator _validator;

        protected DistributedEventBusBase(IEventValidator validator)
        {
            _validator = validator;
        }

        protected void ValidateEvent(string eventName, object? payload)
        {
            _validator.ValidateOrThrow(eventName, payload);
        }

        public abstract Task PublishAsync(IDomainEvent @event, CancellationToken cancellationToken = default);

        public abstract Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default) where TEvent : IDomainEvent;

        public abstract void Subscribe<TEvent, THandler>() where TEvent : IDomainEvent where THandler : IEventHandler<TEvent>;

        public abstract void Unsubscribe<TEvent, THandler>() where TEvent : IDomainEvent where THandler : IEventHandler<TEvent>;
    }
}