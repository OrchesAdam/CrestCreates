using System.Threading;
using System.Threading.Tasks;

namespace CrestCreates.EventBus.Abstract
{
    public interface IEventIdempotencyStore
    {
        Task<bool> IsProcessedAsync(string eventId, CancellationToken cancellationToken = default);
        Task MarkAsProcessedAsync(string eventId, CancellationToken cancellationToken = default);
    }
}
