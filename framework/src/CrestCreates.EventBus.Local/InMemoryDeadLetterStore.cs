using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.EventBus.Abstractions;
using Microsoft.Extensions.Options;

namespace CrestCreates.EventBus.Local;

public class InMemoryDeadLetterStore : IDeadLetterStore
{
    private readonly ConcurrentDictionary<string, DeadLetterMessage> _messages = new();
    private readonly LocalDeadLetterOptions _options;

    public InMemoryDeadLetterStore(IOptions<LocalDeadLetterOptions> options)
    {
        _options = options.Value;
    }

    public Task EnqueueAsync(DeadLetterMessage message, CancellationToken ct)
    {
        if (_messages.Count >= _options.MaxQueueSize)
            return Task.CompletedTask;

        _messages[message.MessageId] = message;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<DeadLetterMessage>> GetPendingAsync(int skip, int take, CancellationToken ct)
    {
        var pending = _messages.Values
            .Where(m => m.Status == DeadLetterStatus.Pending)
            .OrderBy(m => m.FailedAt)
            .Skip(skip)
            .Take(take)
            .ToList();
        return Task.FromResult<IReadOnlyList<DeadLetterMessage>>(pending);
    }

    public Task<DeadLetterMessage?> GetByIdAsync(string messageId, CancellationToken ct)
    {
        _messages.TryGetValue(messageId, out var message);
        return Task.FromResult(message);
    }

    public Task MarkRetryingAsync(string messageId, CancellationToken ct)
    {
        if (_messages.TryGetValue(messageId, out var existing))
            _messages.TryUpdate(messageId,
                existing with { Status = DeadLetterStatus.Retrying },
                existing);
        return Task.CompletedTask;
    }

    public Task MarkRetriedAsync(string messageId, CancellationToken ct)
    {
        if (_messages.TryGetValue(messageId, out var existing))
            _messages.TryUpdate(messageId,
                existing with { Status = DeadLetterStatus.Retried },
                existing);
        return Task.CompletedTask;
    }

    public Task MarkArchivedAsync(string messageId, CancellationToken ct)
    {
        if (_messages.TryGetValue(messageId, out var existing))
            _messages.TryUpdate(messageId,
                existing with { Status = DeadLetterStatus.Archived },
                existing);
        return Task.CompletedTask;
    }

    public Task<int> CountAsync(DeadLetterStatus? status, CancellationToken ct)
    {
        var count = status is null
            ? _messages.Count
            : _messages.Values.Count(m => m.Status == status.Value);
        return Task.FromResult(count);
    }

    public Task<IReadOnlyList<DeadLetterMessage>> GetByEventNameAsync(
        string eventName, int skip, int take, CancellationToken ct)
    {
        var messages = _messages.Values
            .Where(m => m.EventName == eventName)
            .OrderBy(m => m.FailedAt)
            .Skip(skip)
            .Take(take)
            .ToList();
        return Task.FromResult<IReadOnlyList<DeadLetterMessage>>(messages);
    }
}
