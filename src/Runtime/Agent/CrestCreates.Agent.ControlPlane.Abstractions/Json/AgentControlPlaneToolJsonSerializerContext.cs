using System.Text.Json.Serialization;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.ContextPack.Abstractions;
using DraftAbstractions = CrestCreates.DescriptorDraft.Abstractions;

namespace CrestCreates.Agent.ControlPlane.Abstractions.Json;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    GenerationMode = JsonSourceGenerationMode.Metadata)]

// ── Wave 1 — Context/Read ──────────────────────────────────────────────
[JsonSerializable(typeof(AgentToolResult<MetadataContextPack>))]
[JsonSerializable(typeof(MetadataContextPackRequest))]
[JsonSerializable(typeof(AgentToolResult<DescriptorInfo>))]
[JsonSerializable(typeof(DescriptorRef))]
[JsonSerializable(typeof(AgentToolResult<DescriptorSearchResult>))]
[JsonSerializable(typeof(DescriptorSearchRequest))]
[JsonSerializable(typeof(AgentToolResult<DescriptorRelationshipsResult>))]
[JsonSerializable(typeof(AgentToolResult<TopologySummaryResult>))]

// ── Wave 2 — Draft ─────────────────────────────────────────────────────
[JsonSerializable(typeof(AgentToolResult<AgentDescriptorDraftDto>))]
[JsonSerializable(typeof(CreateDescriptorDraftRequest))]
[JsonSerializable(typeof(UpdateDescriptorDraftRequest))]
[JsonSerializable(typeof(AgentToolResult<DescriptorDraftListResult>))]
[JsonSerializable(typeof(DraftAbstractions.DraftQuery))]
[JsonSerializable(typeof(AgentToolResult<DraftComparisonResult>))]
[JsonSerializable(typeof(AgentDescriptorDraftDto))]
[JsonSerializable(typeof(AgentDraftPayloadDto))]
[JsonSerializable(typeof(AgentCapabilityDraftPayloadDto))]
[JsonSerializable(typeof(AgentWorkflowDraftPayloadDto))]
[JsonSerializable(typeof(AgentHumanTaskDraftPayloadDto))]
[JsonSerializable(typeof(AgentFormDraftPayloadDto))]
[JsonSerializable(typeof(AgentEventDraftPayloadDto))]
[JsonSerializable(typeof(AgentSchemaDraftPayloadDto))]
[JsonSerializable(typeof(DescriptorSummaryDto))]

// ── Wave 3 — Review ────────────────────────────────────────────────────
[JsonSerializable(typeof(AgentToolResult<AgentReviewResultDto>))]
[JsonSerializable(typeof(AgentToolResult<DraftAbstractions.DescriptorDraftValidationResult>))]
[JsonSerializable(typeof(AgentToolResult<ReviewResultListResult>))]
[JsonSerializable(typeof(AgentToolResult<DiagnosticExplanation>))]
[JsonSerializable(typeof(ExplainDiagnosticsRequest))]
[JsonSerializable(typeof(AgentReviewResultDto))]
[JsonSerializable(typeof(AgentProposedInventorySummaryDto))]
[JsonSerializable(typeof(AgentTopologySummaryDto))]
[JsonSerializable(typeof(AgentMaterializationSummaryDto))]
[JsonSerializable(typeof(AgentImpactAnalysisSummaryDto))]
[JsonSerializable(typeof(AgentCompatibilitySummaryDto))]
[JsonSerializable(typeof(AgentGovernanceSummaryDto))]

// ── Wave 4 — Fix Proposal ──────────────────────────────────────────────
[JsonSerializable(typeof(AgentToolResult<FixProposalListResult>))]
[JsonSerializable(typeof(AgentToolResult<FixProposal>))]
[JsonSerializable(typeof(ApplyFixProposalRequest))]

// ── Wave 5 — Package Preview ───────────────────────────────────────────
[JsonSerializable(typeof(AgentToolResult<PackageEvidencePreview>))]
[JsonSerializable(typeof(AgentToolResult<ActivationReadinessPreview>))]
[JsonSerializable(typeof(AgentToolResult<DraftAbstractions.DescriptorPackagePreview>))]

// ── Wave 6 — Activation Handoff ────────────────────────────────────────
[JsonSerializable(typeof(AgentToolResult<ActivationRequest>))]
[JsonSerializable(typeof(SubmitActivationRequestRequest))]

// ── Wave 7 — Manifest Query ────────────────────────────────────────────
[JsonSerializable(typeof(AgentToolDescriptor))]
[JsonSerializable(typeof(IReadOnlyList<AgentToolDescriptor>))]

// ── Stable upstream value objects ──────────────────────────────────────
[JsonSerializable(typeof(DescriptorKind))]
[JsonSerializable(typeof(DescriptorState))]
[JsonSerializable(typeof(RelationshipKind))]
[JsonSerializable(typeof(DescriptorStableHashes))]
[JsonSerializable(typeof(DescriptorRelationship))]
[JsonSerializable(typeof(DraftAbstractions.DescriptorDraftOperation))]
[JsonSerializable(typeof(DraftAbstractions.DescriptorDraftStatus))]
[JsonSerializable(typeof(DraftAbstractions.DescriptorDraftAuthorKind))]
[JsonSerializable(typeof(DraftAbstractions.DescriptorDraftDiagnostic))]
[JsonSerializable(typeof(DescriptorPackageEvidence))]

// ── Base types not covered by wave sections above ──────────────────────
[JsonSerializable(typeof(AgentToolResultStatus))]
[JsonSerializable(typeof(AgentToolDiagnostic))]
[JsonSerializable(typeof(AgentToolDiagnosticSeverity))]
[JsonSerializable(typeof(AgentToolInvocationAuditRecord))]
[JsonSerializable(typeof(AgentToolCategory))]
[JsonSerializable(typeof(AgentToolActorKind))]
[JsonSerializable(typeof(AgentToolPermissionRequirement))]
[JsonSerializable(typeof(AgentToolAuthorizationMode))]
[JsonSerializable(typeof(DraftDifference))]
[JsonSerializable(typeof(DraftDifferenceKind))]
[JsonSerializable(typeof(DiagnosticExplanationEntry))]
[JsonSerializable(typeof(FixProposalAction))]
[JsonSerializable(typeof(FixProposalActionKind))]
[JsonSerializable(typeof(FixProposalRiskLevel))]
[JsonSerializable(typeof(ActivationRequestStatus))]
[JsonSerializable(typeof(ActivationReadinessBlocker))]
[JsonSerializable(typeof(ActivationReadinessBlockerSeverity))]

public sealed partial class AgentControlPlaneToolJsonSerializerContext
    : JsonSerializerContext
{
}
