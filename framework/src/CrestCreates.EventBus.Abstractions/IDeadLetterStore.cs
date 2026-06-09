using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CrestCreates.EventBus.Abstractions;

public interface IDeadLetterStore
{
    Task EnqueueAsync(DeadLetterMessage message, CancellationToken ct);
    Task<IReadOnlyList<DeadLetterMessage>> GetPendingAsync(int skip, int take, CancellationToken ct);
    Task<DeadLetterMessage?> GetByIdAsync(string messageId, CancellationToken ct);
    Task MarkRetryingAsync(string messageId, CancellationToken ct);
    Task MarkRetriedAsync(string messageId, CancellationToken ct);
    Task MarkArchivedAsync(string messageId, CancellationToken ct);
    Task<int> CountAsync(DeadLetterStatus? status, CancellationToken ct);
    Task<IReadOnlyList<DeadLetterMessage>> GetByEventNameAsync(string eventName, int skip, int take, CancellationToken ct);
}
