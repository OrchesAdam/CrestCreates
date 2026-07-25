using System.Text.Json.Serialization;
using CrestCreates.Agent.ControlPlane.Abstractions.Activation;
using CrestCreates.Core.Abstractions.Identity;
using CrestCreates.Core.Abstractions.Serialization;
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

[JsonContractSurface(
    typeof(IAgentControlPlaneToolService),
    ExcludedParameterTypes = new[] { typeof(AgentToolInvocationContext) })]
[JsonContractSurface(typeof(IAgentToolManifestProvider))]

// ── Explicit extras: types outside the tool surface that need direct serialization ──

// ── Wave 1 — Context/Read: request types not in surface ──
[JsonContractExplicitRoot(typeof(MetadataContextPackRequest))]

// ── Wave 2 — Draft: DTOs not in surface ──
[JsonContractExplicitRoot(typeof(AgentDescriptorDraftDto))]
[JsonContractExplicitRoot(typeof(AgentDraftPayloadDto))]
[JsonContractExplicitRoot(typeof(AgentDraftPayloadPatchDto))]
[JsonContractExplicitRoot(typeof(AgentCapabilityDraftPayloadDto))]
[JsonContractExplicitRoot(typeof(AgentCapabilityDraftPayloadPatchDto))]
[JsonContractExplicitRoot(typeof(AgentWorkflowDraftPayloadDto))]
[JsonContractExplicitRoot(typeof(AgentWorkflowDraftPayloadPatchDto))]
[JsonContractExplicitRoot(typeof(AgentHumanTaskDraftPayloadDto))]
[JsonContractExplicitRoot(typeof(AgentHumanTaskDraftPayloadPatchDto))]
[JsonContractExplicitRoot(typeof(AgentFormDraftPayloadDto))]
[JsonContractExplicitRoot(typeof(AgentFormDraftPayloadPatchDto))]
[JsonContractExplicitRoot(typeof(AgentEventDraftPayloadDto))]
[JsonContractExplicitRoot(typeof(AgentEventDraftPayloadPatchDto))]
[JsonContractExplicitRoot(typeof(AgentSchemaDraftPayloadDto))]
[JsonContractExplicitRoot(typeof(AgentSchemaDraftPayloadPatchDto))]
[JsonContractExplicitRoot(typeof(DescriptorSummaryDto))]
[JsonContractExplicitRoot(typeof(AgentCapabilityDraftChangedField))]
[JsonContractExplicitRoot(typeof(AgentWorkflowDraftChangedField))]
[JsonContractExplicitRoot(typeof(AgentHumanTaskDraftChangedField))]
[JsonContractExplicitRoot(typeof(AgentFormDraftChangedField))]
[JsonContractExplicitRoot(typeof(AgentEventDraftChangedField))]
[JsonContractExplicitRoot(typeof(AgentSchemaDraftChangedField))]

// ── Wave 3 — Review: DTOs not in surface ──
[JsonContractExplicitRoot(typeof(AgentReviewResultDto))]
[JsonContractExplicitRoot(typeof(AgentProposedInventorySummaryDto))]
[JsonContractExplicitRoot(typeof(AgentTopologySummaryDto))]
[JsonContractExplicitRoot(typeof(AgentMaterializationSummaryDto))]
[JsonContractExplicitRoot(typeof(AgentImpactAnalysisSummaryDto))]
[JsonContractExplicitRoot(typeof(AgentCompatibilitySummaryDto))]
[JsonContractExplicitRoot(typeof(AgentGovernanceSummaryDto))]

// ── Wave 4 — Fix Proposal: DTOs not in surface ──

// ── Wave 5 — Package Preview: DTOs not in surface ──

// ── Wave 6 — Activation Handoff: DTOs not in surface ──

// ── Wave 6.5 — Activation Models (Phase 7e) ──
[JsonContractExplicitRoot(typeof(ActivationBindingSnapshot))]
[JsonContractExplicitRoot(typeof(BindingHashes))]
[JsonContractExplicitRoot(typeof(DescriptorActivationActorKind))]
[JsonContractExplicitRoot(typeof(DescriptorActivationAuditRecord))]
[JsonContractExplicitRoot(typeof(DescriptorActivationDecision))]
[JsonContractExplicitRoot(typeof(DescriptorActivationEligibility))]
[JsonContractExplicitRoot(typeof(DescriptorActivationPolicy))]
[JsonContractExplicitRoot(typeof(DescriptorActivationReviewDecision))]
[JsonContractExplicitRoot(typeof(DescriptorActivationReviewOutcome))]
[JsonContractExplicitRoot(typeof(DescriptorActivationReviewTaskInput))]
[JsonContractExplicitRoot(typeof(ActivationEvidenceRecheckResult))]
[JsonContractExplicitRoot(typeof(ActivationEvidenceDrift))]
[JsonContractExplicitRoot(typeof(RuntimeActivationGateResult))]
[JsonContractExplicitRoot(typeof(ResolvedBindingArtifacts))]

// ── Wave 7 — Manifest Query: DTOs not in surface ──
[JsonContractExplicitRoot(typeof(IReadOnlyList<AgentToolDescriptor>))]

// ── Wave 8 — Review Report (Phase 7d): DTOs not in surface ──
[JsonContractExplicitRoot(typeof(DescriptorReviewReportSectionDto))]
[JsonContractExplicitRoot(typeof(DescriptorReviewReportItemDto))]
[JsonContractExplicitRoot(typeof(DescriptorReviewRecommendationDto))]
[JsonContractExplicitRoot(typeof(DescriptorReviewReportBuildRequest))]
[JsonContractExplicitRoot(typeof(DescriptorReviewReportSectionKind))]
[JsonContractExplicitRoot(typeof(DescriptorReviewRecommendationKind))]
[JsonContractExplicitRoot(typeof(FixProposalKind))]
[JsonContractExplicitRoot(typeof(FixProposalApplicability))]
[JsonContractExplicitRoot(typeof(FixProposalActionSafetyLevel))]
[JsonContractExplicitRoot(typeof(System.Text.Json.JsonElement))]

// ── Stable upstream value objects ──
[JsonContractExplicitRoot(typeof(DescriptorKind))]
[JsonContractExplicitRoot(typeof(DescriptorState))]
[JsonContractExplicitRoot(typeof(RelationshipKind))]
[JsonContractExplicitRoot(typeof(DescriptorStableHashes))]
[JsonContractExplicitRoot(typeof(DescriptorRelationship))]
[JsonContractExplicitRoot(typeof(DraftAbstractions.DescriptorDraftOperation))]
[JsonContractExplicitRoot(typeof(DraftAbstractions.DescriptorDraftStatus))]
[JsonContractExplicitRoot(typeof(DraftAbstractions.DescriptorDraftAuthorKind))]
[JsonContractExplicitRoot(typeof(DraftAbstractions.DescriptorDraftDiagnostic))]
[JsonContractExplicitRoot(typeof(DescriptorPackageEvidence))]

// ── Base types not covered by wave sections above ──
[JsonContractExplicitRoot(typeof(AgentToolResultStatus))]
[JsonContractExplicitRoot(typeof(AgentToolDiagnostic))]
[JsonContractExplicitRoot(typeof(AgentToolInvocationAuditRecord))]
[JsonContractExplicitRoot(typeof(AgentToolCategory))]
[JsonContractExplicitRoot(typeof(AgentToolActorKind))]
[JsonContractExplicitRoot(typeof(AgentToolPermissionRequirement))]
[JsonContractExplicitRoot(typeof(AgentToolAuthorizationMode))]
[JsonContractExplicitRoot(typeof(DraftDifference))]
[JsonContractExplicitRoot(typeof(DraftDifferenceKind))]
[JsonContractExplicitRoot(typeof(DiagnosticExplanationEntry))]
[JsonContractExplicitRoot(typeof(FixProposalAction))]
[JsonContractExplicitRoot(typeof(FixProposalActionKind))]
[JsonContractExplicitRoot(typeof(FixProposalRiskLevel))]
[JsonContractExplicitRoot(typeof(ActivationRequestStatus))]
[JsonContractExplicitRoot(typeof(ActivationReadinessBlocker))]

// ── Core identity types used in diagnostics ──
[JsonContractExplicitRoot(typeof(DiagnosticCode))]
[JsonContractExplicitRoot(typeof(SeverityLevel))]

public sealed partial class AgentControlPlaneToolJsonSerializerContext
    : JsonSerializerContext
{
}
