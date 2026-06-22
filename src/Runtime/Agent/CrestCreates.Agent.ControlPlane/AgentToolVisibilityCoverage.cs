using CrestCreates.Agent.ControlPlane.Abstractions;

namespace CrestCreates.Agent.ControlPlane;

/// <summary>
/// Describes the resource shape a tool operates on, which determines
/// how descriptor kind visibility is enforced.
/// </summary>
internal enum AgentToolResourceShape
{
    /// <summary>No descriptor data — kind visibility is not applicable.</summary>
    None,
    /// <summary>Kind is supplied directly in the request (e.g., CreateDescriptorDraft).</summary>
    DirectKind,
    /// <summary>Single descriptor resolved by reference.</summary>
    SingleDescriptor,
    /// <summary>Single draft resolved by ID.</summary>
    SingleDraft,
    /// <summary>Aggregate query over multiple descriptors or drafts.</summary>
    Aggregate,
    /// <summary>Graph/topology query over descriptor relationships.</summary>
    Graph,
    /// <summary>Context pack built from descriptor traversal.</summary>
    ContextPack,
    /// <summary>Indirect resource owned by a draft (review, fix, preview, activation).</summary>
    Indirect,
    /// <summary>Nested artifact containing derived descriptor-bearing data.</summary>
    Nested
}

/// <summary>
/// Authoritative registry declaring every tool's resource shape.
/// Coverage is bidirectional: every manifest tool has one coverage entry,
/// every coverage entry names an existing manifest tool, and duplicate names fail the test.
/// All 30 manifest tools are complete — no migration guard remains.
/// </summary>
internal sealed record AgentToolVisibilityEntry(
    string ToolName,
    AgentToolResourceShape Shape);

/// <summary>
/// Static, table-driven coverage registry for the Agent Control Plane tool surface.
/// Every tool in <see cref="AgentToolName"/> must have exactly one entry.
/// </summary>
internal static class AgentToolVisibilityCoverage
{
    public static IReadOnlyList<AgentToolVisibilityEntry> All { get; } = BuildCoverage();

    private static IReadOnlyList<AgentToolVisibilityEntry> BuildCoverage() =>
    [
        // ── Context / Read ──
        new(AgentToolName.BuildMetadataContextPack, AgentToolResourceShape.ContextPack),
        new(AgentToolName.BuildRuntimeScenarioContextPack, AgentToolResourceShape.ContextPack),
        new(AgentToolName.GetDescriptorByRef, AgentToolResourceShape.SingleDescriptor),
        new(AgentToolName.SearchDescriptors, AgentToolResourceShape.Aggregate),
        new(AgentToolName.ListDescriptorRelationships, AgentToolResourceShape.Graph),
        new(AgentToolName.GetTopologySummary, AgentToolResourceShape.Graph),

        // ── Draft ──
        new(AgentToolName.CreateDescriptorDraft, AgentToolResourceShape.DirectKind),
        new(AgentToolName.UpdateDescriptorDraft, AgentToolResourceShape.SingleDraft),
        new(AgentToolName.GetDescriptorDraft, AgentToolResourceShape.SingleDraft),
        new(AgentToolName.ListDescriptorDrafts, AgentToolResourceShape.Aggregate),
        new(AgentToolName.CancelDescriptorDraft, AgentToolResourceShape.SingleDraft),
        new(AgentToolName.CompareDescriptorDraft, AgentToolResourceShape.Nested),

        // ── Review ──
        new(AgentToolName.ValidateDescriptorDraft, AgentToolResourceShape.SingleDraft),
        new(AgentToolName.ReviewDescriptorDraft, AgentToolResourceShape.Nested),
        new(AgentToolName.GetDraftReviewResult, AgentToolResourceShape.Indirect),
        new(AgentToolName.ListDraftReviewResults, AgentToolResourceShape.Indirect),
        new(AgentToolName.ExplainDiagnostics, AgentToolResourceShape.Indirect),

        // ── Fix Proposal ──
        new(AgentToolName.SuggestDescriptorDraftFixes, AgentToolResourceShape.Nested),
        new(AgentToolName.GetFixProposal, AgentToolResourceShape.Indirect),
        new(AgentToolName.ListFixProposals, AgentToolResourceShape.Indirect),
        new(AgentToolName.ApplyFixProposalToDraft, AgentToolResourceShape.Indirect),

        // ── Package Preview ──
        new(AgentToolName.PreviewDescriptorPackage, AgentToolResourceShape.Nested),
        new(AgentToolName.BuildPackageEvidencePreview, AgentToolResourceShape.Nested),
        new(AgentToolName.BuildActivationReadinessPreview, AgentToolResourceShape.Nested),
        new(AgentToolName.GetPackagePreview, AgentToolResourceShape.Indirect),

        // ── Activation Handoff ──
        new(AgentToolName.SubmitActivationRequest, AgentToolResourceShape.Indirect),
        new(AgentToolName.GetActivationRequestStatus, AgentToolResourceShape.Indirect),
        new(AgentToolName.CancelActivationRequest, AgentToolResourceShape.Indirect),

        // ── Review Report (Phase 7d) ──
        new(AgentToolName.BuildDescriptorReviewReport, AgentToolResourceShape.Indirect),
        new(AgentToolName.RenderDescriptorReviewReport, AgentToolResourceShape.Indirect),

        // ── Manifest ──
        new(AgentToolName.ListAgentTools, AgentToolResourceShape.None),
        new(AgentToolName.GetAgentToolDescriptor, AgentToolResourceShape.None)
    ];
}
