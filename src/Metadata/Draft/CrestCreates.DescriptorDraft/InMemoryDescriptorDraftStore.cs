using System.Collections.Concurrent;
using CrestCreates.DescriptorDraft.Abstractions;
using Draft = CrestCreates.DescriptorDraft.Abstractions.DescriptorDraft;

namespace CrestCreates.DescriptorDraft;

public sealed class InMemoryDescriptorDraftStore : IDescriptorDraftStore
{
    private readonly ConcurrentDictionary<(string TenantId, string DraftId), Draft> _drafts = new();

    public Task SaveAsync(Draft draft, CancellationToken ct = default)
    {
        _drafts[(draft.TenantId, draft.DraftId)] = draft.Snapshot();
        return Task.CompletedTask;
    }

    public Task<Draft?> GetAsync(string tenantId, string draftId, CancellationToken ct = default)
    {
        if (_drafts.TryGetValue((tenantId, draftId), out var existing))
            return Task.FromResult<Draft?>(existing.Snapshot());
        return Task.FromResult<Draft?>(null);
    }

    public Task<IReadOnlyList<Draft>> ListAsync(string tenantId, DraftQuery? query = null, CancellationToken ct = default)
    {
        IEnumerable<Draft> results = _drafts.Values
            .Where(d => d.TenantId == tenantId);

        if (query is not null)
        {
            if (query.DescriptorKind.HasValue)
                results = results.Where(d => d.DescriptorKind == query.DescriptorKind.Value);
            if (query.Operation.HasValue)
                results = results.Where(d => d.Operation == query.Operation.Value);
            if (query.AuthorKind.HasValue)
                results = results.Where(d => d.AuthorKind == query.AuthorKind.Value);
            if (query.Status.HasValue)
                results = results.Where(d => d.Status == query.Status.Value);
            if (query.CreatedFrom.HasValue)
                results = results.Where(d => d.CreatedAt >= query.CreatedFrom.Value);
            if (query.CreatedTo.HasValue)
                results = results.Where(d => d.CreatedAt <= query.CreatedTo.Value);
        }

        var list = results.Select(d => d.Snapshot()).ToList().AsReadOnly();
        return Task.FromResult((IReadOnlyList<Draft>)list);
    }
}
