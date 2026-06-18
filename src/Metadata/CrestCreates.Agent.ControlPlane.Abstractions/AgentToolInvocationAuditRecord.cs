using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Agent.ControlPlane.Abstractions;

public sealed record AgentToolInvocationAuditRecord
{
    public required string AuditId { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
    public required AgentToolInvocationContext Context { get; init; }
    public required AgentToolResultStatus ResultStatus { get; init; }
    public required IReadOnlyList<AgentToolDiagnostic> Diagnostics { get; init; }
    public string? InputSummaryHash { get; init; }
    public IReadOnlyList<DescriptorRef>? TouchedDescriptorRefs { get; init; }
    public IReadOnlyList<string>? TouchedDraftIds { get; init; }
    public IReadOnlyList<string>? TouchedReviewResultIds { get; init; }
    public IReadOnlyList<string>? TouchedFixProposalIds { get; init; }
    public IReadOnlyList<string>? TouchedPackagePreviewIds { get; init; }
    public IReadOnlyList<string>? TouchedActivationRequestIds { get; init; }
}
