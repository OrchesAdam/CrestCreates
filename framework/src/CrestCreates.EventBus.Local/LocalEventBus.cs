using System.Threading;
using System.Threading.Tasks;
using MediatR;
using CrestCreates.Domain.DomainEvents;
using CrestCreates.EventBus.Abstract;

namespace CrestCreates.EventBus.Local
{
    public class LocalEventBus : IEventBus
    {
        private readonly IMediator _mediator;

        public LocalEventBus(IMediator mediator)
        {
            _mediator = mediator;
        }

        public async Task PublishAsync(IDomainEvent @event, CancellationToken cancellationToken = default)
        {
            await _mediator.Publish(@event, cancellationToken);
        }

        public async Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default) where TEvent : IDomainEvent
        {
            await _mediator.Publish(@event, cancellationToken);
        }

        public void Subscribe<TEvent, THandler>() where TEvent : IDomainEvent where THandler : IEventHandler<TEvent>
        {
            // MediatR 会自动处理订阅，这里不需要额外操作
        }

        public void Unsubscribe<TEvent, THandler>() where TEvent : IDomainEvent where THandler : IEventHandler<TEvent>
        {
            // MediatR 会自动处理取消订阅，这里不需要额外操作
        }
    }
}