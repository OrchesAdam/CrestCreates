using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.EventBus.Abstract;

namespace CrestCreates.EventBus.Local
{
    public class InMemoryEventIdempotencyStore : IEventIdempotencyStore
    {
        private readonly ConcurrentDictionary<string, byte> _processedEvents = new();

        public Task<bool> IsProcessedAsync(string eventId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_processedEvents.ContainsKey(eventId));
        }

        public Task MarkAsProcessedAsync(string eventId, CancellationToken cancellationToken = default)
        {
            _processedEvents.TryAdd(eventId, 0);
            return Task.CompletedTask;
        }
    }
}
