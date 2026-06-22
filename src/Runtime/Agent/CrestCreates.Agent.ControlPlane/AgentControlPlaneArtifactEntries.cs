using CrestCreates.Agent.ControlPlane.Abstractions;
using CrestCreates.DescriptorDraft.Abstractions;
using Draft = CrestCreates.DescriptorDraft.Abstractions.DescriptorDraft;
using DraftReviewResult = CrestCreates.DescriptorDraft.Abstractions.DescriptorDraftReviewResult;
using DraftPackagePreview = CrestCreates.DescriptorDraft.Abstractions.DescriptorPackagePreview;

namespace CrestCreates.Agent.ControlPlane;

/// <summary>
/// Shared tenant-qualified artifact storage entries for review results,
/// fix proposals, package previews, evidence previews, and activation
/// requests. These types are the single storage record for each artifact
/// kind and carry the originating DraftId for owner-kind resolution.
/// </summary>

internal sealed record ReviewResourceSnapshot(
    DraftReviewResult Review,
    Draft Owner,
    DateTimeOffset CreatedAt);

internal sealed record FixProposalResourceSnapshot(
    FixProposal Proposal,
    Draft Owner);

internal sealed record PackagePreviewEntry(
    string DraftId,
    string TenantId,
    DraftPackagePreview Preview);

internal sealed record PackagePreviewResourceSnapshot(
    PackagePreviewEntry Preview,
    Draft Owner);

internal sealed record EvidencePreviewEntry(
    string DraftId,
    string TenantId,
    PackageEvidencePreview Preview);

internal sealed record EvidencePreviewResourceSnapshot(
    EvidencePreviewEntry Evidence,
    Draft Owner);

internal sealed record ReportResourceSnapshot(
    DescriptorReviewReportDto Report,
    Draft Owner);

internal sealed record ActivationResourceSnapshot(
    ActivationRequest Request,
    Draft Owner);
