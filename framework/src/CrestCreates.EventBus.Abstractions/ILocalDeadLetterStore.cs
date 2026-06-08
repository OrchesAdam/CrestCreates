using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CrestCreates.EventBus.Abstractions;

public interface ILocalDeadLetterStore
{
    Task EnqueueAsync(DeadLetterMessage message, CancellationToken cancellationToken = default);
    Task<DeadLetterMessage?> GetAsync(string messageId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DeadLetterMessage>> ListAsync(
        string? eventType = null,
        DeadLetterStatus? status = null,
        int skip = 0,
        int take = 50,
        CancellationToken cancellationToken = default);
    Task MarkRetryingAsync(string messageId, CancellationToken cancellationToken = default);
    Task MarkRetriedAsync(string messageId, CancellationToken cancellationToken = default);
    Task RemoveAsync(string messageId, CancellationToken cancellationToken = default);
    Task<int> CountAsync(
        string? eventType = null,
        DeadLetterStatus? status = null,
        CancellationToken cancellationToken = default);
}
