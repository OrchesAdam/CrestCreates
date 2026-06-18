using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Domain.DomainEvents;
using CrestCreates.EventBus.Abstractions;

namespace CrestCreates.EventBus.Local
{
    public class DomainEventPublisher : IDomainEventPublisher
    {
        private readonly ILocalEventBus _localEventBus;

        public DomainEventPublisher(ILocalEventBus localEventBus)
        {
            _localEventBus = localEventBus;
        }

        public async Task PublishAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default)
        {
            await _localEventBus.PublishAsync(domainEvent, cancellationToken);
        }

        public async Task PublishAsync<TEvent>(TEvent domainEvent, CancellationToken cancellationToken = default) where TEvent : IDomainEvent
        {
            await _localEventBus.PublishAsync(domainEvent, cancellationToken);
        }
    }
}
