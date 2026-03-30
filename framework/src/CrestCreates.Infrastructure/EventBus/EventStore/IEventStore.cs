using System;using System.Collections.Generic;using System.Threading;using System.Threading.Tasks;using CrestCreates.Domain.DomainEvents;

namespace CrestCreates.Infrastructure.EventBus.EventStore
{
    public interface IEventStore
    {
        Task<long> AppendEventAsync(IDomainEvent @event, CancellationToken cancellationToken = default);
        Task<IEnumerable<IDomainEvent>> GetEventsAsync(Guid aggregateId, int fromVersion = 0, CancellationToken cancellationToken = default);
        Task<IEnumerable<IDomainEvent>> GetEventsAsync(Type aggregateType, Guid aggregateId, int fromVersion = 0, CancellationToken cancellationToken = default);
        Task<long> GetNextVersionAsync(Guid aggregateId, CancellationToken cancellationToken = default);
    }

    public interface IEventStoreSerializer
    {
        string Serialize(IDomainEvent @event);
        IDomainEvent Deserialize(string data, Type eventType);
    }
}