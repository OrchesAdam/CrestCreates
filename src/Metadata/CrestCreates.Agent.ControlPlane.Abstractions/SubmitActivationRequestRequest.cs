namespace CrestCreates.Agent.ControlPlane.Abstractions;

public sealed record SubmitActivationRequestRequest
{
    public required string DraftId { get; init; }
    public string? ReviewResultId { get; init; }
    public string? PackagePreviewId { get; init; }
    public string? EvidencePreviewId { get; init; }
    public string? CorrelationId { get; init; }
    public string? Rationale { get; init; }
}
