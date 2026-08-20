using System.Collections.Concurrent;
using CrestCreates.DescriptorDraft.Abstractions;
using Draft = CrestCreates.DescriptorDraft.Abstractions.DescriptorDraft;

namespace CrestCreates.DescriptorDraft;

public sealed class InMemoryDescriptorDraftStore : IDescriptorDraftStore
{
    private readonly ConcurrentDictionary<(string TenantId, string DraftId), Draft> _drafts = new();

    public Task SaveAsync(Draft draft, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        DescriptorDraftStoreSemantics.ValidateSaveInput(draft);
        DescriptorDraftPayloadSupport.EnsureSupported(draft.Payload);
        _drafts[(draft.TenantId, draft.DraftId)] = draft.Snapshot();
        return Task.CompletedTask;
    }

    public Task<Draft?> GetAsync(string tenantId, string draftId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        DescriptorDraftStoreSemantics.ValidateGetInput(tenantId, draftId);
        if (_drafts.TryGetValue((tenantId, draftId), out var existing))
            return Task.FromResult<Draft?>(existing.Snapshot());
        return Task.FromResult<Draft?>(null);
    }

    public Task<IReadOnlyList<Draft>> ListAsync(string tenantId, DraftQuery? query = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        DescriptorDraftStoreSemantics.ValidateListInput(tenantId, query);

        IEnumerable<Draft> results = _drafts.Values
            .Where(d => d.TenantId == tenantId);

        if (query is not null)
            results = results.Where(d => DescriptorDraftStoreSemantics.MatchesQuery(d, query));

        var list = DescriptorDraftStoreSemantics.OrderDrafts(results)
            .Select(d => d.Snapshot())
            .ToList()
            .AsReadOnly();

        return Task.FromResult((IReadOnlyList<Draft>)list);
    }
}
