using System.Text.Json.Serialization;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Metadata.Abstractions.DescriptorPackage;
using CrestCreates.Metadata.Abstractions.DescriptorRelationship;
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
[JsonSerializable(typeof(AgentDraftPayloadPatchDto))]
[JsonSerializable(typeof(AgentCapabilityDraftPayloadDto))]
[JsonSerializable(typeof(AgentCapabilityDraftPayloadPatchDto))]
[JsonSerializable(typeof(AgentWorkflowDraftPayloadDto))]
[JsonSerializable(typeof(AgentWorkflowDraftPayloadPatchDto))]
[JsonSerializable(typeof(AgentHumanTaskDraftPayloadDto))]
[JsonSerializable(typeof(AgentHumanTaskDraftPayloadPatchDto))]
[JsonSerializable(typeof(AgentFormDraftPayloadDto))]
[JsonSerializable(typeof(AgentFormDraftPayloadPatchDto))]
[JsonSerializable(typeof(AgentEventDraftPayloadDto))]
[JsonSerializable(typeof(AgentEventDraftPayloadPatchDto))]
[JsonSerializable(typeof(AgentSchemaDraftPayloadDto))]
[JsonSerializable(typeof(AgentSchemaDraftPayloadPatchDto))]
[JsonSerializable(typeof(DescriptorSummaryDto))]
[JsonSerializable(typeof(AgentCapabilityDraftChangedField))]
[JsonSerializable(typeof(AgentWorkflowDraftChangedField))]
[JsonSerializable(typeof(AgentHumanTaskDraftChangedField))]
[JsonSerializable(typeof(AgentFormDraftChangedField))]
[JsonSerializable(typeof(AgentEventDraftChangedField))]
[JsonSerializable(typeof(AgentSchemaDraftChangedField))]

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

// ── Wave 8 — Review Report (Phase 7d) ──────────────────────────────────
[JsonSerializable(typeof(AgentToolResult<DescriptorReviewReportDto>))]
[JsonSerializable(typeof(DescriptorReviewReportDto))]
[JsonSerializable(typeof(DescriptorReviewReportSectionDto))]
[JsonSerializable(typeof(DescriptorReviewReportItemDto))]
[JsonSerializable(typeof(DescriptorReviewRecommendationDto))]
[JsonSerializable(typeof(DescriptorReviewReportBuildRequest))]
[JsonSerializable(typeof(DescriptorReviewReportSectionKind))]
[JsonSerializable(typeof(DescriptorReviewSeverity))]
[JsonSerializable(typeof(DescriptorReviewRecommendationKind))]
[JsonSerializable(typeof(DescriptorReviewReportFormat))]
[JsonSerializable(typeof(FixProposalKind))]
[JsonSerializable(typeof(FixProposalApplicability))]
[JsonSerializable(typeof(FixProposalActionSafetyLevel))]
[JsonSerializable(typeof(System.Text.Json.JsonElement))]

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
