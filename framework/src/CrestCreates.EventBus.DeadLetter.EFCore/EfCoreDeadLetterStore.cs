using CrestCreates.Event.Abstractions;
using CrestCreates.EventBus.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace CrestCreates.EventBus.DeadLetter.EFCore;

public sealed class EfCoreDeadLetterStore : IDeadLetterStore
{
    private readonly DeadLetterDbContext _db;

    public EfCoreDeadLetterStore(DeadLetterDbContext db) => _db = db;

    public async Task EnqueueAsync(DeadLetterMessage message, CancellationToken ct)
    {
        _db.DeadLetters.Add(new DeadLetterEntity
        {
            MessageId = message.MessageId,
            EventName = message.EventName,
            EventVersion = message.EventVersion,
            EventDescriptorId = message.EventDescriptorId,
            CorrelationId = message.CorrelationId,
            TenantId = message.TenantId,
            Scope = message.Scope.ToString(),
            PayloadTypeFullName = message.PayloadTypeFullName,
            Payload = message.Payload,
            ErrorMessage = message.ErrorMessage,
            ExceptionType = message.ExceptionType,
            OccurredAt = message.OccurredAt,
            FailedAt = message.FailedAt,
            RetryCount = message.RetryCount,
            MaxRetries = message.MaxRetries,
            Status = message.Status.ToString()
        });
        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<DeadLetterMessage>> GetPendingAsync(int skip, int take, CancellationToken ct)
    {
        var entities = await _db.DeadLetters
            .Where(e => e.Status == DeadLetterStatus.Pending.ToString())
            .OrderBy(e => e.FailedAt)
            .Skip(skip).Take(take)
            .ToListAsync(ct);
        return entities.Select(ToMessage).ToList();
    }

    public async Task<DeadLetterMessage?> GetByIdAsync(string messageId, CancellationToken ct)
    {
        var entity = await _db.DeadLetters.FindAsync([messageId], ct);
        return entity is null ? null : ToMessage(entity);
    }

    public async Task MarkRetryingAsync(string messageId, CancellationToken ct)
        => await UpdateStatus(messageId, DeadLetterStatus.Retrying, ct);

    public async Task MarkRetriedAsync(string messageId, CancellationToken ct)
        => await UpdateStatus(messageId, DeadLetterStatus.Retried, ct);

    public async Task MarkArchivedAsync(string messageId, CancellationToken ct)
        => await UpdateStatus(messageId, DeadLetterStatus.Archived, ct);

    public async Task<int> CountAsync(DeadLetterStatus? status, CancellationToken ct)
    {
        var query = _db.DeadLetters.AsQueryable();
        if (status is not null)
            query = query.Where(e => e.Status == status.Value.ToString());
        return await query.CountAsync(ct);
    }

    public async Task<IReadOnlyList<DeadLetterMessage>> GetByEventNameAsync(
        string eventName, int skip, int take, CancellationToken ct)
    {
        var entities = await _db.DeadLetters
            .Where(e => e.EventName == eventName)
            .OrderBy(e => e.FailedAt)
            .Skip(skip).Take(take)
            .ToListAsync(ct);
        return entities.Select(ToMessage).ToList();
    }

    private async Task UpdateStatus(string messageId, DeadLetterStatus status, CancellationToken ct)
    {
        var entity = await _db.DeadLetters.FindAsync([messageId], ct);
        if (entity is not null)
        {
            entity.Status = status.ToString();
            await _db.SaveChangesAsync(ct);
        }
    }

    private static DeadLetterMessage ToMessage(DeadLetterEntity e)
        => new(
            e.MessageId,
            e.EventName,
            e.EventVersion,
            e.EventDescriptorId,
            e.CorrelationId,
            e.TenantId,
            Enum.Parse<EventScope>(e.Scope),
            e.PayloadTypeFullName,
            e.Payload,
            e.ErrorMessage,
            e.ExceptionType,
            e.OccurredAt,
            e.FailedAt,
            e.RetryCount,
            e.MaxRetries,
            Enum.Parse<DeadLetterStatus>(e.Status));
}
