using System.Threading;
using System.Threading.Tasks;
using MediatR;
using CrestCreates.Domain.DomainEvents;

namespace CrestCreates.EventBus.Local
{
    public class DomainEventPublisher : IDomainEventPublisher
    {
        private readonly IMediator _mediator;

        public DomainEventPublisher(IMediator mediator)
        {
            _mediator = mediator;
        }

        public async Task PublishAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default)
        {
            await _mediator.Publish(domainEvent, cancellationToken);
        }

        public async Task PublishAsync<TEvent>(TEvent domainEvent, CancellationToken cancellationToken = default) where TEvent : IDomainEvent
        {
            await _mediator.Publish(domainEvent, cancellationToken);
        }
    }
}