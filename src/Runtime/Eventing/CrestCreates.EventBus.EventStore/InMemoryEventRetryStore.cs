using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using CrestCreates.Domain.DomainEvents;
using CrestCreates.EventBus.Abstract;

namespace CrestCreates.EventBus.EventStore
{
    public class InMemoryEventRetryStore : IEventRetryStore
    {
        private readonly ConcurrentDictionary<Guid, (IDomainEvent Event, int RetryCount)> _retryEvents = new();

        public async Task AddRetryEventAsync(IDomainEvent @event, int retryCount, CancellationToken cancellationToken = default)
        {
            // 使用新的Guid作为键，因为IDomainEvent没有Id属性
            _retryEvents.TryAdd(Guid.NewGuid(), (@event, retryCount));
            await Task.CompletedTask;
        }

        public async Task<IEnumerable<(IDomainEvent Event, int RetryCount)>> GetRetryEventsAsync(CancellationToken cancellationToken = default)
        {
            return await Task.FromResult(_retryEvents.Values);
        }

        public async Task RemoveRetryEventAsync(Guid eventId, CancellationToken cancellationToken = default)
        {
            _retryEvents.TryRemove(eventId, out _);
            await Task.CompletedTask;
        }
    }
}