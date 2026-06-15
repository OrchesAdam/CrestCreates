namespace CrestCreates.Samples.DescriptorControlPlane;

/// <summary>
/// Domain data types for the Company Certification runtime execution plane.
/// No persistence, no ORM - pure in-memory model for the golden scenario.
/// </summary>

public sealed record CertificationSubmitInput(
    string CompanyName,
    string UnifiedSocialCreditCode,
    string CertificationType,
    string? ApplicationDate,
    string? Notes);

public sealed record CertificationReviewInput(
    string? CertificationId,
    string ReviewerNotes,
    string Decision);

public sealed record CertificationResult(
    string CertificationId,
    string Status,
    string Message);

public enum CertificationStatus
{
    Submitted,
    UnderReview,
    Approved,
    Rejected
}

public sealed record CertificationRecord
{
    public required Guid Id { get; init; }
    public required string CompanyName { get; init; }
    public required string UnifiedSocialCreditCode { get; init; }
    public required string CertificationType { get; init; }
    public string? ApplicationDate { get; init; }
    public string? Notes { get; init; }
    public CertificationStatus Status { get; set; } = CertificationStatus.Submitted;
    public string? ReviewerNotes { get; set; }
    public string? ReviewerDecision { get; set; }
    public string? ReviewedBy { get; set; }
}
