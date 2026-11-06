using System.Collections.Concurrent;
using CrestCreates.Draft.Abstractions;

namespace CrestCreates.Draft;

public sealed class InMemoryDraftStore : IDraftStore
{
    private readonly ConcurrentDictionary<string, DraftRecord> _drafts = new();

    public Task<DraftRecord> SaveAsync(DraftRecord draft, CancellationToken ct = default)
    {
        var stored = new DraftRecord
        {
            DraftId = draft.DraftId,
            DraftType = draft.DraftType,
            Schema = draft.Schema,
            TenantId = draft.TenantId,
            OwnerId = draft.OwnerId,
            PayloadJson = draft.PayloadJson,
            Status = draft.Status,
            CreatedAt = draft.CreatedAt == default ? DateTimeOffset.UtcNow : draft.CreatedAt,
            UpdatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = draft.ExpiresAt
        };
        _drafts[stored.DraftId] = stored;
        return Task.FromResult(stored);
    }

    public Task<DraftRecord?> GetAsync(string draftId, CancellationToken ct = default)
    {
        _drafts.TryGetValue(draftId, out var draft);
        return Task.FromResult(draft);
    }

    public Task DeleteAsync(string draftId, CancellationToken ct = default)
    {
        _drafts.TryRemove(draftId, out _);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<DraftRecord>> QueryAsync(DraftQuery query, CancellationToken ct = default)
    {
        var results = _drafts.Values.AsEnumerable();

        if (query.TenantId != null)
            results = results.Where(d => d.TenantId == query.TenantId);
        if (query.OwnerId != null)
            results = results.Where(d => d.OwnerId == query.OwnerId);
        if (query.DraftType != null)
            results = results.Where(d => d.DraftType == query.DraftType);
        if (query.Status != null)
            results = results.Where(d => d.Status == query.Status.Value);

        if (query.MaxResults.HasValue)
            results = results.Take(query.MaxResults.Value);

        return Task.FromResult<IReadOnlyList<DraftRecord>>(results.ToList().AsReadOnly());
    }
}
