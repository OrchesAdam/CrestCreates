using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Metadata.Abstractions.DescriptorCompatibility;
using CrestCreates.Metadata.Abstractions.DescriptorImpact;
using CrestCreates.Metadata.Abstractions.DescriptorLifecycle;
using CrestCreates.Metadata.Abstractions.DescriptorTopology;

namespace CrestCreates.DescriptorDraft.Abstractions;

public sealed record DescriptorDraftReviewResult
{
    public required string DraftId { get; init; }
    public required string TenantId { get; init; }
    public required DescriptorDraftValidationResult ValidationResult { get; init; }
    public DescriptorDraftMaterializationResult? MaterializationResult { get; init; }
    public IReadOnlyList<IDescriptor>? ProposedInventory { get; init; }
    public DescriptorTopologySnapshot? TopologySnapshot { get; init; }
    public DescriptorImpactAnalysisReport? ImpactAnalysisResult { get; init; }
    public DescriptorCompatibilityReport? CompatibilityResult { get; init; }
    public DescriptorLifecycleGovernanceReport? GovernanceDecision { get; init; }
    public DescriptorStableHashes? StableHashes { get; init; }
    public DescriptorPackagePreview? PackagePreview { get; init; }
    public required IReadOnlyList<DescriptorDraftDiagnostic> Diagnostics { get; init; }
    public required bool IsActivationEligible { get; init; }
}
