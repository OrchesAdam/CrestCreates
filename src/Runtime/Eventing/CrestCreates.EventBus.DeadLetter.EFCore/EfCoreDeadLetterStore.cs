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
        await using var transaction = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            // Upsert keyed by MessageId: the same call inserts the initial record and persists every
            // later state transition. A plain Add would collide on the primary key / tracked entity.
            var entity = await _db.DeadLetters.FindAsync([message.MessageId], ct);
            if (entity is null)
            {
                entity = new DeadLetterEntity { MessageId = message.MessageId };
                _db.DeadLetters.Add(entity);
            }

            Apply(entity, message);

            await _db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            throw;
        }
    }

    private static void Apply(DeadLetterEntity entity, DeadLetterMessage message)
    {
        entity.EventName = message.EventName;
        entity.EventVersion = message.EventVersion;
        entity.EventDescriptorId = message.EventDescriptorId;
        entity.CorrelationId = message.CorrelationId;
        entity.TenantId = message.TenantId;
        entity.Scope = message.Scope.ToString();
        entity.PayloadTypeFullName = message.PayloadTypeFullName;
        entity.Payload = message.Payload;
        entity.ErrorMessage = message.ErrorMessage;
        entity.ExceptionType = message.ExceptionType;
        entity.OccurredAt = message.OccurredAt;
        entity.FailedAt = message.FailedAt;
        entity.RetryCount = message.RetryCount;
        entity.MaxRetries = message.MaxRetries;
        entity.Status = message.Status.ToString();
        entity.LocalReplayKey = message.LocalReplayKey;
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
        await using var transaction = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            var entity = await _db.DeadLetters.FindAsync([messageId], ct);
            if (entity is not null)
            {
                entity.Status = status.ToString();
                await _db.SaveChangesAsync(ct);
            }
            await transaction.CommitAsync(ct);
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            throw;
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
            Enum.Parse<DeadLetterStatus>(e.Status),
            e.LocalReplayKey);
}
