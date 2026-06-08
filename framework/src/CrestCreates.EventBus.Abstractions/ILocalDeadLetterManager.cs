using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CrestCreates.EventBus.Abstractions;

public sealed record DeadLetterStats(
    int TotalCount,
    int PendingCount,
    int RetryingCount,
    int RetriedCount,
    int ArchivedCount);

public sealed record DeadLetterRetryResult(
    string MessageId,
    bool Success,
    string? ErrorMessage);

public interface ILocalDeadLetterManager
{
    Task<IReadOnlyList<DeadLetterMessage>> ListAsync(
        string? eventType = null,
        DeadLetterStatus? status = null,
        int skip = 0,
        int take = 50,
        CancellationToken cancellationToken = default);
    Task<DeadLetterRetryResult> RetryAsync(string messageId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DeadLetterRetryResult>> RetryAllAsync(CancellationToken cancellationToken = default);
    Task<int> ClearAsync(string? eventType = null, CancellationToken cancellationToken = default);
    Task<DeadLetterStats> GetStatsAsync(CancellationToken cancellationToken = default);
}
