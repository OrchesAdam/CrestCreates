namespace CrestCreates.Agent.ControlPlane.Abstractions.Activation;

/// <summary>
/// Immutable binding snapshot captured at activation request creation time.
/// Replaces the previous optional single-reference model (ReviewResultId/PackagePreviewId/EvidencePreviewId).
/// At activation time, the request service rechecks current state against these bound values.
/// Any mismatch transitions the request to Stale and prevents activation.
/// </summary>
public sealed record ActivationBindingSnapshot
{
    public required string TenantId { get; init; }
    public required string DraftId { get; init; }
    public required int DraftVersion { get; init; }
    public required string ReviewResultId { get; init; }
    public string? ReportId { get; init; }
    public required string PackagePreviewId { get; init; }
    public required string EvidencePreviewId { get; init; }
    public required BindingHashes Hashes { get; init; }
    public string? CorrelationId { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
}
