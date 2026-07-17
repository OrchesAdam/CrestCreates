using System.Collections.Concurrent;

namespace CrestCreates.Samples.DescriptorControlPlane;

public sealed class InMemoryCompanyCertificationStore : ICompanyCertificationStore
{
    private readonly ConcurrentDictionary<Guid, CertificationRecord> _records = new();

    public Task<CertificationRecord> CreateAsync(
        CertificationSubmitInput input,
        CancellationToken cancellationToken = default)
    {
        var record = new CertificationRecord
        {
            Id = Guid.NewGuid(),
            CompanyName = input.CompanyName,
            UnifiedSocialCreditCode = input.UnifiedSocialCreditCode,
            CertificationType = input.CertificationType,
            ApplicationDate = input.ApplicationDate,
            Notes = input.Notes,
            Status = CertificationStatus.Submitted,
        };
        _records[record.Id] = record;
        return Task.FromResult(record);
    }

    public Task<CertificationRecord?> GetAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var record = _records.TryGetValue(id, out var r) ? r : null;
        return Task.FromResult(record);
    }

    public Task<IReadOnlyList<CertificationRecord>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<CertificationRecord>>(_records.Values.ToList().AsReadOnly());
    }

    public Task ApproveAsync(
        Guid id,
        CertificationReviewInput review,
        string reviewerUserId,
        CancellationToken cancellationToken = default)
    {
        if (_records.TryGetValue(id, out var existing))
        {
            var updated = existing with
            {
                Status = CertificationStatus.Approved,
                ReviewerNotes = review.ReviewerNotes,
                ReviewerDecision = review.Decision,
                ReviewedBy = reviewerUserId,
            };
            _records[id] = updated;
        }
        return Task.CompletedTask;
    }

    public Task RejectAsync(
        Guid id,
        CertificationReviewInput review,
        string reviewerUserId,
        CancellationToken cancellationToken = default)
    {
        if (_records.TryGetValue(id, out var existing))
        {
            var updated = existing with
            {
                Status = CertificationStatus.Rejected,
                ReviewerNotes = review.ReviewerNotes,
                ReviewerDecision = review.Decision,
                ReviewedBy = reviewerUserId,
            };
            _records[id] = updated;
        }
        return Task.CompletedTask;
    }

    public Task<int> CountAsync(
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_records.Count);
    }

    [Obsolete("Use CreateAsync instead. Will be removed after migration.")]
    public CertificationRecord Create(CertificationSubmitInput input) =>
        CreateAsync(input).GetAwaiter().GetResult();

    [Obsolete("Use GetAsync instead. Will be removed after migration.")]
    public CertificationRecord? Get(Guid id) =>
        GetAsync(id).GetAwaiter().GetResult();

    [Obsolete("Use ApproveAsync instead. Will be removed after migration.")]
    public void Approve(Guid id, CertificationReviewInput review, string reviewerUserId) =>
        ApproveAsync(id, review, reviewerUserId).GetAwaiter().GetResult();

    [Obsolete("Use RejectAsync instead. Will be removed after migration.")]
    public void Reject(Guid id, CertificationReviewInput review, string reviewerUserId) =>
        RejectAsync(id, review, reviewerUserId).GetAwaiter().GetResult();

    public int Count => _records.Count;

    [Obsolete("Use GetAllAsync instead. Will be removed after migration.")]
    public IReadOnlyList<CertificationRecord> GetAll() => _records.Values.ToList().AsReadOnly();

    public void Clear() => _records.Clear();
}
