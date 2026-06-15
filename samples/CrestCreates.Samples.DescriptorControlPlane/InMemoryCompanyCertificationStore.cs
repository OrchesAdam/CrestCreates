using System.Collections.Concurrent;

namespace CrestCreates.Samples.DescriptorControlPlane;

public sealed class InMemoryCompanyCertificationStore
{
    private readonly ConcurrentDictionary<Guid, CertificationRecord> _records = new();

    public CertificationRecord Create(CertificationSubmitInput input)
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
        return record;
    }

    public CertificationRecord? Get(Guid id) =>
        _records.TryGetValue(id, out var record) ? record : null;

    public void Approve(Guid id, CertificationReviewInput review, string reviewerUserId)
    {
        if (_records.TryGetValue(id, out var record))
        {
            record.Status = CertificationStatus.Approved;
            record.ReviewerNotes = review.ReviewerNotes;
            record.ReviewerDecision = review.Decision;
            record.ReviewedBy = reviewerUserId;
        }
    }

    public void Reject(Guid id, CertificationReviewInput review, string reviewerUserId)
    {
        if (_records.TryGetValue(id, out var record))
        {
            record.Status = CertificationStatus.Rejected;
            record.ReviewerNotes = review.ReviewerNotes;
            record.ReviewerDecision = review.Decision;
            record.ReviewedBy = reviewerUserId;
        }
    }

    public int Count => _records.Count;

    public IReadOnlyList<CertificationRecord> GetAll() => _records.Values.ToList().AsReadOnly();

    public void Clear() => _records.Clear();
}
