namespace CrestCreates.Draft.Abstractions;

public interface IDraftStore
{
    Task<DraftRecord> SaveAsync(DraftRecord draft, CancellationToken ct = default);
    Task<DraftRecord?> GetAsync(string draftId, CancellationToken ct = default);
    Task DeleteAsync(string draftId, CancellationToken ct = default);
    Task<IReadOnlyList<DraftRecord>> QueryAsync(DraftQuery query, CancellationToken ct = default);
}