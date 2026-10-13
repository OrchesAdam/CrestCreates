using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.EventBus.Abstractions;
using Microsoft.Extensions.Logging;

namespace CrestCreates.EventBus.Local;

/// <summary>
/// Manual dead-letter entry point. Single and batch retries delegate to the shared replay
/// executor; the manager does not duplicate decoding, state transitions or dispatch logic.
/// </summary>
public class DefaultLocalDeadLetterManager : ILocalDeadLetterManager
{
    private readonly IDeadLetterStore _store;
    private readonly ILocalDeadLetterReplayExecutor _executor;
    private readonly ILogger<DefaultLocalDeadLetterManager>? _logger;

    public DefaultLocalDeadLetterManager(
        IDeadLetterStore store,
        ILocalDeadLetterReplayExecutor executor,
        ILogger<DefaultLocalDeadLetterManager>? logger = null)
    {
        _store = store;
        _executor = executor;
        _logger = logger;
    }

    public async Task<IReadOnlyList<DeadLetterMessage>> ListAsync(
        string? eventType = null,
        DeadLetterStatus? status = null,
        int skip = 0,
        int take = 50,
        CancellationToken cancellationToken = default)
    {
        if (eventType is not null)
            return await _store.GetByEventNameAsync(eventType, skip, take, cancellationToken);
        if (status == DeadLetterStatus.Pending)
            return await _store.GetPendingAsync(skip, take, cancellationToken);
        return Array.Empty<DeadLetterMessage>();
    }

    public async Task<DeadLetterRetryResult> RetryAsync(
        string messageId, CancellationToken cancellationToken = default)
    {
        var message = await _store.GetByIdAsync(messageId, cancellationToken);
        if (message is null)
        {
            return new DeadLetterRetryResult(messageId, false, "Message not found");
        }

        return await _executor.ExecuteAsync(message, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<DeadLetterRetryResult>> RetryAllAsync(
        CancellationToken cancellationToken = default)
    {
        var pending = await _store.GetPendingAsync(0, int.MaxValue, cancellationToken);

        var results = new List<DeadLetterRetryResult>();

        foreach (var message in pending)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            try
            {
                results.Add(await _executor.ExecuteAsync(message, cancellationToken).ConfigureAwait(false));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
        }

        return results.AsReadOnly();
    }

    public async Task<int> ClearAsync(
        string? eventType = null, CancellationToken cancellationToken = default)
    {
        var retried = await _store.GetByEventNameAsync(
            eventType ?? "", 0, int.MaxValue, cancellationToken);
        // Mark all retried/archived messages — for the InMemory store, we just count
        var toRemove = retried
            .Where(m => m.Status is DeadLetterStatus.Retried or DeadLetterStatus.Archived)
            .ToList();

        foreach (var message in toRemove)
        {
            await _store.MarkArchivedAsync(message.MessageId, cancellationToken);
        }

        _logger?.LogInformation(
            "Cleared {Count} dead letter messages (eventType: {EventType})",
            toRemove.Count, eventType ?? "all");

        return toRemove.Count;
    }

    public async Task<DeadLetterStats> GetStatsAsync(CancellationToken cancellationToken = default)
    {
        var total = await _store.CountAsync(null, cancellationToken);
        var pending = await _store.CountAsync(DeadLetterStatus.Pending, cancellationToken);
        var retrying = await _store.CountAsync(DeadLetterStatus.Retrying, cancellationToken);
        var retried = await _store.CountAsync(DeadLetterStatus.Retried, cancellationToken);
        var archived = await _store.CountAsync(DeadLetterStatus.Archived, cancellationToken);

        return new DeadLetterStats(total, pending, retrying, retried, archived);
    }
}
