using CrestCreates.DescriptorDraft.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorCompatibility;
using CrestCreates.Metadata.Abstractions.DescriptorImpact;
using CrestCreates.Metadata.Abstractions.DescriptorLifecycle;
using CrestCreates.Metadata.Abstractions.DescriptorTopology;

namespace CrestCreates.Samples.DescriptorControlPlane.Authoring;

public sealed record CompanyCertificationDraftSetReviewResult
{
    public required DescriptorDraftSet DraftSet { get; init; }
    public required IReadOnlyList<DescriptorDraftReviewResult> PerDraftReviewResults { get; init; }
    public required IReadOnlyList<IDescriptor> FinalProposedInventory { get; init; }
    public required bool IsBlocked { get; init; }
    public required string FinalDecisionSource { get; init; }
    public string? BlockReason { get; init; }
    public DescriptorTopologySnapshot? FinalTopology { get; init; }
    public DescriptorLifecycleGovernanceReport? FinalGovernance { get; init; }
    public DescriptorImpactAnalysisReport? FinalImpact { get; init; }
    public DescriptorCompatibilityReport? FinalCompat { get; init; }
}
