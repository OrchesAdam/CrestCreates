using DraftAbstractions = CrestCreates.DescriptorDraft.Abstractions;
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Agent.ControlPlane.Abstractions;

/// <summary>
/// Adapter-safe projection of DescriptorDraftReviewResult.
/// Replaces DescriptorDraftReviewResult in all tool results.
/// IsActivationEligible is an agent-facing readiness signal derived after
/// #40 visibility projection. It is NOT an activation approval, NOT a
/// governance decision, and NOT an execution authorization.
/// </summary>
public sealed record AgentReviewResultDto
{
    public required string DraftId { get; init; }
    public required string TenantId { get; init; }
    public required DraftAbstractions.DescriptorDraftValidationResult ValidationResult { get; init; }
    public AgentMaterializationSummaryDto? MaterializationSummary { get; init; }
    public AgentProposedInventorySummaryDto? ProposedInventorySummary { get; init; }
    public AgentTopologySummaryDto? TopologySummary { get; init; }
    public AgentImpactAnalysisSummaryDto? ImpactAnalysisSummary { get; init; }
    public AgentCompatibilitySummaryDto? CompatibilitySummary { get; init; }
    public AgentGovernanceSummaryDto? GovernanceSummary { get; init; }
    public DescriptorStableHashes? StableHashes { get; init; }
    public DraftAbstractions.DescriptorPackagePreview? PackagePreview { get; init; }
    public required IReadOnlyList<DraftAbstractions.DescriptorDraftDiagnostic> Diagnostics { get; init; }
    public required bool IsActivationEligible { get; init; }
}
