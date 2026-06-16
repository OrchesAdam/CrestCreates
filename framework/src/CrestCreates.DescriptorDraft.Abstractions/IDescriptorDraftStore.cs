namespace CrestCreates.DescriptorDraft.Abstractions;

public interface IDescriptorDraftStore
{
    Task SaveAsync(DescriptorDraft draft, CancellationToken ct = default);
    Task<DescriptorDraft?> GetAsync(string tenantId, string draftId, CancellationToken ct = default);
    Task<IReadOnlyList<DescriptorDraft>> ListAsync(string tenantId, DraftQuery? query = null, CancellationToken ct = default);
}
