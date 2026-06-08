using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.EventBus.Abstractions;
using Microsoft.Extensions.Options;

namespace CrestCreates.EventBus.Local.Channel;

public class InMemoryDeadLetterStore : ILocalDeadLetterStore
{
    private readonly ConcurrentDictionary<string, DeadLetterMessage> _messages = new();
    private readonly LocalDeadLetterOptions _options;

    public InMemoryDeadLetterStore(IOptions<LocalDeadLetterOptions> options)
    {
        _options = options.Value;
    }

    public Task EnqueueAsync(DeadLetterMessage message, CancellationToken cancellationToken = default)
    {
        if (_messages.Count >= _options.MaxQueueSize)
            return Task.CompletedTask;

        _messages[message.MessageId] = message;
        return Task.CompletedTask;
    }

    public Task<DeadLetterMessage?> GetAsync(string messageId, CancellationToken cancellationToken = default)
    {
        _messages.TryGetValue(messageId, out var message);
        return Task.FromResult(message);
    }

    public Task<IReadOnlyList<DeadLetterMessage>> ListAsync(
        string? eventType = null,
        DeadLetterStatus? status = null,
        int skip = 0,
        int take = 50,
        CancellationToken cancellationToken = default)
    {
        var query = _messages.Values.AsEnumerable();

        if (eventType is not null)
            query = query.Where(m => m.EventType == eventType);

        if (status.HasValue)
            query = query.Where(m => m.Status == status.Value);

        var result = query
            .OrderByDescending(m => m.FailedAt)
            .Skip(skip)
            .Take(take)
            .ToList()
            .AsReadOnly();

        return Task.FromResult<IReadOnlyList<DeadLetterMessage>>(result);
    }

    public Task MarkRetryingAsync(string messageId, CancellationToken cancellationToken = default)
    {
        _messages.TryGetValue(messageId, out var existing);
        if (existing is not null)
        {
            _messages.TryUpdate(messageId,
                existing with { Status = DeadLetterStatus.Retrying },
                existing);
        }
        return Task.CompletedTask;
    }

    public Task MarkRetriedAsync(string messageId, CancellationToken cancellationToken = default)
    {
        _messages.TryGetValue(messageId, out var existing);
        if (existing is not null)
        {
            _messages.TryUpdate(messageId,
                existing with { Status = DeadLetterStatus.Retried },
                existing);
        }
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string messageId, CancellationToken cancellationToken = default)
    {
        _messages.TryRemove(messageId, out _);
        return Task.CompletedTask;
    }

    public Task<int> CountAsync(
        string? eventType = null,
        DeadLetterStatus? status = null,
        CancellationToken cancellationToken = default)
    {
        var query = _messages.Values.AsEnumerable();

        if (eventType is not null)
            query = query.Where(m => m.EventType == eventType);

        if (status.HasValue)
            query = query.Where(m => m.Status == status.Value);

        return Task.FromResult(query.Count());
    }
}
