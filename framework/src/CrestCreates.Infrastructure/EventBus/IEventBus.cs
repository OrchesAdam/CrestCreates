using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Domain.DomainEvents;

namespace CrestCreates.Infrastructure.EventBus
{
    public interface IEventBus
    {
        Task PublishAsync(IDomainEvent @event, CancellationToken cancellationToken = default);
        Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default) where TEvent : IDomainEvent;
        void Subscribe<TEvent, THandler>() where TEvent : IDomainEvent where THandler : IEventHandler<TEvent>;
        void Unsubscribe<TEvent, THandler>() where TEvent : IDomainEvent where THandler : IEventHandler<TEvent>;
    }

    public interface IEventHandler<in TEvent> where TEvent : IDomainEvent
    {
        Task HandleAsync(TEvent @event, CancellationToken cancellationToken = default);
    }
}