namespace CrestCreates.Samples.DescriptorControlPlane;

public interface ICompanyCertificationStore
{
    Task<CertificationRecord> CreateAsync(
        CertificationSubmitInput input,
        CancellationToken cancellationToken = default);

    Task<CertificationRecord?> GetAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CertificationRecord>> GetAllAsync(
        CancellationToken cancellationToken = default);

    Task ApproveAsync(
        Guid id,
        CertificationReviewInput review,
        string reviewerUserId,
        CancellationToken cancellationToken = default);

    Task RejectAsync(
        Guid id,
        CertificationReviewInput review,
        string reviewerUserId,
        CancellationToken cancellationToken = default);

    Task<int> CountAsync(
        CancellationToken cancellationToken = default);
}
