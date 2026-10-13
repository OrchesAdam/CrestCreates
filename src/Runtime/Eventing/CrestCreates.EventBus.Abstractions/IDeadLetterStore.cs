using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CrestCreates.EventBus.Abstractions;

public interface IDeadLetterStore
{
    /// <summary>
    /// Inserts or replaces the full message, keyed by <see cref="DeadLetterMessage.MessageId"/>.
    /// This single write is used both for the initial record and for every subsequent state
    /// transition (retrying, retried, archived, retry-count/error updates): implementations must
    /// be an idempotent upsert, never an insert-only enqueue.
    /// </summary>
    Task EnqueueAsync(DeadLetterMessage message, CancellationToken ct);
    Task<IReadOnlyList<DeadLetterMessage>> GetPendingAsync(int skip, int take, CancellationToken ct);
    Task<DeadLetterMessage?> GetByIdAsync(string messageId, CancellationToken ct);
    Task MarkRetryingAsync(string messageId, CancellationToken ct);
    Task MarkRetriedAsync(string messageId, CancellationToken ct);
    Task MarkArchivedAsync(string messageId, CancellationToken ct);
    Task<int> CountAsync(DeadLetterStatus? status, CancellationToken ct);
    Task<IReadOnlyList<DeadLetterMessage>> GetByEventNameAsync(string eventName, int skip, int take, CancellationToken ct);
}
