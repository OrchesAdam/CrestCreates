using CrestCreates.Agent.ControlPlane.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorCompatibility;
using CrestCreates.Metadata.Abstractions.DescriptorImpact;
using CrestCreates.Metadata.Abstractions.DescriptorLifecycle;
using CrestCreates.Metadata.Abstractions.DescriptorTopology;
using DraftAbstractions = CrestCreates.DescriptorDraft.Abstractions;

namespace CrestCreates.Agent.ControlPlane.Projections;

/// <summary>
/// Projects DescriptorDraftReviewResult to adapter-safe AgentReviewResultDto.
/// Visibility closure contract: this helper filters denied descriptor kinds
/// from all summary fields. It must not re-introduce hidden refs from the
/// full ProposedInventory / full TopologySnapshot.
/// Lives in ControlPlane (not Abstractions) because it depends on domain types.
/// </summary>
internal static class AgentReviewResultDtoProjection
{
    /// <summary>
    /// Projects with no visibility filtering. Use only when the source
    /// has already been filtered through #40 visibility closure.
    /// </summary>
    public static AgentReviewResultDto Project(DraftAbstractions.DescriptorDraftReviewResult source)
    {
        return Project(source, deniedKinds: null);
    }

    /// <summary>
    /// Projects with visibility filtering. Denied descriptor kinds are
    /// excluded from ProposedInventory, Topology, and ImpactAnalysis summaries.
    /// </summary>
    public static AgentReviewResultDto Project(
        DraftAbstractions.DescriptorDraftReviewResult source,
        IReadOnlySet<DescriptorKind>? deniedKinds)
    {
        var proposedInventory = deniedKinds is not null
            ? FilterByKind(source.ProposedInventory, deniedKinds)
            : source.ProposedInventory;

        var topologySnapshot = source.TopologySnapshot;
        var impactAnalysis = source.ImpactAnalysisResult;
        var affectedDescriptors = deniedKinds is not null && impactAnalysis is not null
            ? impactAnalysis.AffectedDescriptors.Where(a => !deniedKinds.Contains(a.Kind)).ToList()
            : impactAnalysis?.AffectedDescriptors;

        return new AgentReviewResultDto
        {
            DraftId = source.DraftId,
            TenantId = source.TenantId,
            ValidationResult = source.ValidationResult,
            MaterializationSummary = MapMaterializationResult(source.MaterializationResult, deniedKinds),
            ProposedInventorySummary = MapProposedInventory(proposedInventory),
            TopologySummary = MapTopologySnapshot(topologySnapshot, deniedKinds),
            ImpactAnalysisSummary = MapImpactAnalysisResult(impactAnalysis, affectedDescriptors),
            CompatibilitySummary = MapCompatibilityResult(source.CompatibilityResult),
            GovernanceSummary = MapGovernanceDecision(source.GovernanceDecision),
            StableHashes = source.StableHashes,
            PackagePreview = source.PackagePreview,
            Diagnostics = source.Diagnostics,
            IsActivationEligible = source.IsActivationEligible,
        };
    }

    private static IReadOnlyList<IDescriptor>? FilterByKind(
        IReadOnlyList<IDescriptor>? descriptors, IReadOnlySet<DescriptorKind> deniedKinds)
    {
        if (descriptors is null) return null;
        return descriptors.Where(d => !deniedKinds.Contains(d.Kind)).ToList();
    }

    private static AgentMaterializationSummaryDto? MapMaterializationResult(
        DraftAbstractions.DescriptorDraftMaterializationResult? result,
        IReadOnlySet<DescriptorKind>? deniedKinds)
    {
        if (result is null)
            return null;

        var proposedInventory = deniedKinds is not null
            ? result.ProposedInventory.Where(d => !deniedKinds.Contains(d.Kind)).ToList()
            : result.ProposedInventory;

        return new AgentMaterializationSummaryDto
        {
            IsMaterialized = result.IsMaterialized,
            ProposedInventoryRefs = proposedInventory
                .Select(d => new DescriptorRef(d.Namespace, d.Id))
                .ToList(),
            Diagnostics = result.Diagnostics,
        };
    }

    private static AgentProposedInventorySummaryDto? MapProposedInventory(
        IReadOnlyList<IDescriptor>? inventory)
    {
        if (inventory is null)
            return null;

        return new AgentProposedInventorySummaryDto
        {
            DescriptorRefs = inventory
                .Select(d => new DescriptorRef(d.Namespace, d.Id))
                .ToList(),
            TotalCount = inventory.Count,
            CountsByKind = inventory
                .GroupBy(d => d.Kind)
                .ToDictionary(g => g.Key, g => g.Count()),
        };
    }

    private static AgentTopologySummaryDto? MapTopologySnapshot(
        DescriptorTopologySnapshot? snapshot,
        IReadOnlySet<DescriptorKind>? deniedKinds)
    {
        if (snapshot is null)
            return null;

        var nodes = deniedKinds is not null
            ? snapshot.Nodes.Values.Where(n => !deniedKinds.Contains(n.Kind)).ToList()
            : snapshot.Nodes.Values.ToList();

        // When filtering by denied kinds, edges must also be filtered:
        // exclude edges whose source or target nodes belong to denied kinds.
        var visibleNodeRefs = deniedKinds is not null
            ? nodes.Select(n => n.Ref).ToHashSet()
            : null;

        var edges = deniedKinds is not null
            ? snapshot.Edges.Where(e =>
                visibleNodeRefs is null ||
                (visibleNodeRefs.Contains(e.From) && visibleNodeRefs.Contains(e.To)))
                .ToList()
            : snapshot.Edges.ToList();

        return new AgentTopologySummaryDto
        {
            TotalNodeCount = nodes.Count,
            TotalEdgeCount = edges.Count,
            NodeCountsByKind = nodes
                .GroupBy(n => n.Kind)
                .ToDictionary(g => g.Key, g => g.Count()),
            EdgeCountsByKind = edges
                .GroupBy(e => e.Kind)
                .ToDictionary(g => g.Key, g => g.Count()),
        };
    }

    private static AgentImpactAnalysisSummaryDto? MapImpactAnalysisResult(
        DescriptorImpactAnalysisReport? report,
        IReadOnlyList<AffectedDescriptor>? filteredAffected)
    {
        if (report is null)
            return null;

        return new AgentImpactAnalysisSummaryDto
        {
            AffectedDescriptors = (filteredAffected ?? report.AffectedDescriptors)
                .Select(a => a.Ref)
                .ToList(),
            TotalAffectedCount = (filteredAffected ?? report.AffectedDescriptors).Count,
            Severity = report.MaxSeverity.ToString(),
            Summary = null,
        };
    }

    private static AgentCompatibilitySummaryDto? MapCompatibilityResult(
        DescriptorCompatibilityReport? report)
    {
        if (report is null)
            return null;

        return new AgentCompatibilitySummaryDto
        {
            IsCompatible = !report.HasBreakingChanges && !report.HasUnsupportedFindings,
            IncompatibilityCount = report.Findings
                .Count(f => f.Level == DescriptorCompatibilityLevel.Breaking
                         || f.Level == DescriptorCompatibilityLevel.Unsupported),
            Summary = null,
        };
    }

    private static AgentGovernanceSummaryDto? MapGovernanceDecision(
        DescriptorLifecycleGovernanceReport? report)
    {
        if (report is null)
            return null;

        return new AgentGovernanceSummaryDto
        {
            IsApproved = report.MaxDecision == DescriptorLifecycleDecisionKind.Allowed,
            Decision = report.MaxDecision.ToString(),
            Rationale = null,
        };
    }
}
