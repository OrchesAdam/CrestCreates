using CrestCreates.Draft.Abstractions;
using CrestCreates.MultiTenancy.Abstract;

namespace CrestCreates.Draft;

public sealed class TenantIsolatedDraftStore : IDraftStore
{
    private readonly IDraftStore _inner;
    private readonly ITenantContext? _tenantContext;

    public TenantIsolatedDraftStore(IDraftStore inner, ITenantContext? tenantContext = null)
    {
        _inner = inner;
        _tenantContext = tenantContext;
    }

    public async Task<DraftRecord> SaveAsync(DraftRecord draft, CancellationToken ct = default)
    {
        if (_tenantContext?.CurrentTenantId != null)
        {
            draft = new DraftRecord
            {
                DraftId = draft.DraftId,
                DraftType = draft.DraftType,
                Schema = draft.Schema,
                TenantId = _tenantContext.CurrentTenantId,
                OwnerId = draft.OwnerId,
                PayloadJson = draft.PayloadJson,
                Status = draft.Status,
                CreatedAt = draft.CreatedAt,
                UpdatedAt = draft.UpdatedAt,
                ExpiresAt = draft.ExpiresAt
            };
        }
        return await _inner.SaveAsync(draft, ct).ConfigureAwait(false);
    }

    public async Task<DraftRecord?> GetAsync(string draftId, CancellationToken ct = default)
    {
        var draft = await _inner.GetAsync(draftId, ct).ConfigureAwait(false);
        if (draft == null) return null;
        if (_tenantContext?.CurrentTenantId != null
            && draft.TenantId != _tenantContext.CurrentTenantId)
            return null;
        return draft;
    }

    public Task DeleteAsync(string draftId, CancellationToken ct = default)
        => _inner.DeleteAsync(draftId, ct);

    public Task<IReadOnlyList<DraftRecord>> QueryAsync(DraftQuery query, CancellationToken ct = default)
    {
        if (_tenantContext?.CurrentTenantId != null)
            query = new DraftQuery
            {
                TenantId = _tenantContext.CurrentTenantId,
                OwnerId = query.OwnerId,
                DraftType = query.DraftType,
                Status = query.Status,
                MaxResults = query.MaxResults
            };
        return _inner.QueryAsync(query, ct);
    }
}
