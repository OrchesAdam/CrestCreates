namespace CrestCreates.Agent.ControlPlane.Abstractions;

public sealed record ActivationRequest
{
    public required string RequestId { get; init; }
    public required string TenantId { get; init; }
    public required string DraftId { get; init; }
    public required ActivationRequestStatus Status { get; init; }
    public required DateTimeOffset SubmittedAt { get; init; }
    public required string SubmittedBy { get; init; }
    public string? ReviewResultId { get; init; }
    public string? PackagePreviewId { get; init; }
    public string? EvidencePreviewId { get; init; }
    public string? CorrelationId { get; init; }
    public IReadOnlyList<AgentToolDiagnostic>? Diagnostics { get; init; }
}
