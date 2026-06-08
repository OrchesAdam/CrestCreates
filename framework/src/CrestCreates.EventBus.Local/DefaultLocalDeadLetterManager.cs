using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.EventBus.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CrestCreates.EventBus.Local;

public class DefaultLocalDeadLetterManager : ILocalDeadLetterManager
{
    private readonly ILocalDeadLetterStore _store;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DefaultLocalDeadLetterManager> _logger;

    public DefaultLocalDeadLetterManager(
        ILocalDeadLetterStore store,
        IServiceScopeFactory scopeFactory,
        ILogger<DefaultLocalDeadLetterManager> logger)
    {
        _store = store;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public Task<IReadOnlyList<DeadLetterMessage>> ListAsync(
        string? eventType = null,
        DeadLetterStatus? status = null,
        int skip = 0,
        int take = 50,
        CancellationToken cancellationToken = default)
    {
        return _store.ListAsync(eventType, status, skip, take, cancellationToken);
    }

    public async Task<DeadLetterRetryResult> RetryAsync(
        string messageId, CancellationToken cancellationToken = default)
    {
        var message = await _store.GetAsync(messageId, cancellationToken);
        if (message is null)
        {
            return new DeadLetterRetryResult(messageId, false, "Message not found");
        }

        await _store.MarkRetryingAsync(messageId, cancellationToken);

        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var dispatcher = scope.ServiceProvider.GetRequiredService<ILocalEventDispatcher>();

            var eventType = Type.GetType(message.EventType);
            if (eventType is null)
            {
                return new DeadLetterRetryResult(messageId, false,
                    $"Cannot resolve event type: {message.EventType}");
            }

            var eventData = JsonSerializer.Deserialize(
                message.Payload, eventType);

            if (eventData is ILocalEvent localEvent)
            {
                await dispatcher.DispatchAsync(localEvent, cancellationToken);
            }

            await _store.MarkRetriedAsync(messageId, cancellationToken);
            _logger.LogInformation(
                "Manually retried dead letter message {MessageId} successfully", messageId);

            return new DeadLetterRetryResult(messageId, true, null);
        }
        catch (Exception ex)
        {
            var newRetryCount = message.RetryCount + 1;
            var updatedMessage = message with
            {
                RetryCount = newRetryCount,
                Status = newRetryCount >= message.MaxRetries
                    ? DeadLetterStatus.Archived
                    : DeadLetterStatus.Pending,
                ErrorMessage = ex.Message
            };
            await _store.EnqueueAsync(updatedMessage, cancellationToken);

            _logger.LogError(ex,
                "Manual retry failed for dead letter message {MessageId}", messageId);

            return new DeadLetterRetryResult(messageId, false, ex.Message);
        }
    }

    public async Task<IReadOnlyList<DeadLetterRetryResult>> RetryAllAsync(
        CancellationToken cancellationToken = default)
    {
        var pending = await _store.ListAsync(
            status: DeadLetterStatus.Pending,
            take: int.MaxValue,
            cancellationToken: cancellationToken);

        var results = new List<DeadLetterRetryResult>();

        foreach (var message in pending)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            var result = await RetryAsync(message.MessageId, cancellationToken);
            results.Add(result);
        }

        return results.AsReadOnly();
    }

    public async Task<int> ClearAsync(
        string? eventType = null, CancellationToken cancellationToken = default)
    {
        var retried = await _store.ListAsync(
            eventType: eventType,
            status: DeadLetterStatus.Retried,
            take: int.MaxValue,
            cancellationToken: cancellationToken);

        var archived = await _store.ListAsync(
            eventType: eventType,
            status: DeadLetterStatus.Archived,
            take: int.MaxValue,
            cancellationToken: cancellationToken);

        var toRemove = retried.Concat(archived).ToList();
        foreach (var message in toRemove)
        {
            await _store.RemoveAsync(message.MessageId, cancellationToken);
        }

        _logger.LogInformation(
            "Cleared {Count} dead letter messages (eventType: {EventType})",
            toRemove.Count, eventType ?? "all");

        return toRemove.Count;
    }

    public async Task<DeadLetterStats> GetStatsAsync(CancellationToken cancellationToken = default)
    {
        var total = await _store.CountAsync(cancellationToken: cancellationToken);
        var pending = await _store.CountAsync(status: DeadLetterStatus.Pending, cancellationToken: cancellationToken);
        var retrying = await _store.CountAsync(status: DeadLetterStatus.Retrying, cancellationToken: cancellationToken);
        var retried = await _store.CountAsync(status: DeadLetterStatus.Retried, cancellationToken: cancellationToken);
        var archived = await _store.CountAsync(status: DeadLetterStatus.Archived, cancellationToken: cancellationToken);

        return new DeadLetterStats(total, pending, retrying, retried, archived);
    }
}
