namespace CrestCreates.Agent.ControlPlane.Abstractions;

/// <summary>
/// Canonical tool name constants for the Agent Control Plane tool surface.
/// Every tool name must match the corresponding entry in <see cref="IAgentToolManifestProvider"/>.
/// The <see cref="DefaultAgentControlPlaneToolService"/> validates that the caller-supplied
/// <see cref="AgentToolInvocationContext.ToolName"/> matches the expected tool name
/// before performing manifest lookup, authorization, or audit recording.
/// </summary>
public static class AgentToolName
{
    // ── Context / Read ──
    public const string BuildMetadataContextPack = nameof(BuildMetadataContextPack);
    public const string BuildRuntimeScenarioContextPack = nameof(BuildRuntimeScenarioContextPack);
    public const string GetDescriptorByRef = nameof(GetDescriptorByRef);
    public const string SearchDescriptors = nameof(SearchDescriptors);
    public const string ListDescriptorRelationships = nameof(ListDescriptorRelationships);
    public const string GetTopologySummary = nameof(GetTopologySummary);

    // ── Draft ──
    public const string CreateDescriptorDraft = nameof(CreateDescriptorDraft);
    public const string UpdateDescriptorDraft = nameof(UpdateDescriptorDraft);
    public const string GetDescriptorDraft = nameof(GetDescriptorDraft);
    public const string ListDescriptorDrafts = nameof(ListDescriptorDrafts);
    public const string CancelDescriptorDraft = nameof(CancelDescriptorDraft);
    public const string CompareDescriptorDraft = nameof(CompareDescriptorDraft);

    // ── Review ──
    public const string ValidateDescriptorDraft = nameof(ValidateDescriptorDraft);
    public const string ReviewDescriptorDraft = nameof(ReviewDescriptorDraft);
    public const string GetDraftReviewResult = nameof(GetDraftReviewResult);
    public const string ListDraftReviewResults = nameof(ListDraftReviewResults);
    public const string ExplainDiagnostics = nameof(ExplainDiagnostics);

    // ── Fix Proposal ──
    public const string SuggestDescriptorDraftFixes = nameof(SuggestDescriptorDraftFixes);
    public const string GetFixProposal = nameof(GetFixProposal);
    public const string ListFixProposals = nameof(ListFixProposals);
    public const string ApplyFixProposalToDraft = nameof(ApplyFixProposalToDraft);

    // ── Package Preview ──
    public const string PreviewDescriptorPackage = nameof(PreviewDescriptorPackage);
    public const string BuildPackageEvidencePreview = nameof(BuildPackageEvidencePreview);
    public const string BuildActivationReadinessPreview = nameof(BuildActivationReadinessPreview);
    public const string GetPackagePreview = nameof(GetPackagePreview);

    // ── Activation Handoff ──
    public const string SubmitActivationRequest = nameof(SubmitActivationRequest);
    public const string GetActivationRequestStatus = nameof(GetActivationRequestStatus);
    public const string CancelActivationRequest = nameof(CancelActivationRequest);

    // ── Manifest ──
    public const string ListAgentTools = nameof(ListAgentTools);
    public const string GetAgentToolDescriptor = nameof(GetAgentToolDescriptor);
}
