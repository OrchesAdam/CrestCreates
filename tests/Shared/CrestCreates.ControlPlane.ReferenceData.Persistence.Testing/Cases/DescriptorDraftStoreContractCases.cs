using CrestCreates.DescriptorDraft.Abstractions;
using Draft = CrestCreates.DescriptorDraft.Abstractions.DescriptorDraft;

namespace CrestCreates.ControlPlane.ReferenceData.Persistence.Testing;

/// <summary>
/// Runner-free Draft store contract primitives. Concrete runners provide the
/// same store through <see cref="IDescriptorDraftStoreContractDriver"/>.
/// </summary>
public static class DescriptorDraftStoreContractCases
{
    public static async Task SaveReadDetachedAsync(IDescriptorDraftStore store, Draft draft)
    {
        await store.SaveAsync(draft);
        var first = await store.GetAsync(draft.TenantId, draft.DraftId)
            ?? throw new InvalidOperationException("The saved draft was not readable.");
        var second = await store.GetAsync(draft.TenantId, draft.DraftId)
            ?? throw new InvalidOperationException("The saved draft was not readable twice.");
        if (ReferenceEquals(first, second))
            throw new InvalidOperationException("Draft reads must return detached snapshots.");
    }
}
