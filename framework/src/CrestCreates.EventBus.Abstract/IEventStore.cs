using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using CrestCreates.Domain.DomainEvents;

namespace CrestCreates.EventBus.Abstract
{
    public interface IEventStore
    {
        Task StoreEventAsync(IDomainEvent @event, CancellationToken cancellationToken = default);
        Task<IEnumerable<IDomainEvent>> GetEventsAsync(CancellationToken cancellationToken = default);
        Task MarkEventAsProcessedAsync(Guid eventId, CancellationToken cancellationToken = default);
    }

    public interface IEventStoreSerializer
    {
        string Serialize(IDomainEvent @event);
        IDomainEvent Deserialize(string json, Type eventType);
    }

    public interface IEventRetryStore
    {
        Task AddRetryEventAsync(IDomainEvent @event, int retryCount, CancellationToken cancellationToken = default);
        Task<IEnumerable<(IDomainEvent Event, int RetryCount)>> GetRetryEventsAsync(CancellationToken cancellationToken = default);
        Task RemoveRetryEventAsync(Guid eventId, CancellationToken cancellationToken = default);
    }
}