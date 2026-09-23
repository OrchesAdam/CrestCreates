using CrestCreates.Core.Abstractions.Identity;
using CrestCreates.DescriptorDraft.Abstractions;
using CrestCreates.DescriptorDraft.Abstractions.CanonicalHashing;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Metadata.Abstractions.DescriptorCompatibility;
using CrestCreates.Metadata.Abstractions.DescriptorImpact;
using CrestCreates.Metadata.Abstractions.DescriptorLifecycle;
using CrestCreates.Metadata.Abstractions.DescriptorTopology;

namespace CrestCreates.Agent.ControlPlane.Abstractions;

/// <summary>
/// Finite captured facts used to render a review report. This snapshot is data,
/// not evidence that its source was visible or authorized.
/// </summary>
public sealed record DescriptorReviewReportInputSnapshot
{
    public const int CurrentVersion = 1;

    public required int Version { get; init; }
    public required string TenantId { get; init; }
    public required string DraftId { get; init; }
    public required bool IsActivationEligible { get; init; }
    public required bool IsValid { get; init; }
    public required IReadOnlyList<DescriptorDraftDiagnostic> ValidationDiagnostics { get; init; }
    public required IReadOnlyList<DescriptorDraftDiagnostic> ReviewDiagnostics { get; init; }
    public required DescriptorReviewReportOwnerInput Owner { get; init; }
    public required DescriptorReviewReportMaterializationInput? Materialization { get; init; }
    public required DescriptorReviewReportTopologyInput? Topology { get; init; }
    public required DescriptorReviewReportImpactInput? Impact { get; init; }
    public required IReadOnlyList<DescriptorReviewReportCompatibilityFindingInput>? CompatibilityFindings { get; init; }
    public required DescriptorLifecycleDecisionKind? GovernanceMaxDecision { get; init; }
    public required DescriptorLifecycleDecisionKind? GovernanceFirstDecision { get; init; }
    public required DescriptorLifecycleTransition? GovernanceFirstTransition { get; init; }
    public required IReadOnlyList<string>? GovernancePackageFindingSubjectIds { get; init; }
    public required DescriptorReviewReportPackagePreviewInput? PackagePreview { get; init; }
    public required DescriptorStableHashes? StableHashes { get; init; }

    public bool GovernanceIsAllowed => GovernanceMaxDecision == DescriptorLifecycleDecisionKind.Allowed;
    public bool GovernanceRequiresReview => GovernanceMaxDecision == DescriptorLifecycleDecisionKind.ReviewRequired;
    public bool GovernanceIsBlocked => GovernanceMaxDecision == DescriptorLifecycleDecisionKind.Blocked;

    /// <summary>Captures only facts required by the report, after visibility filtering.</summary>
    public static DescriptorReviewReportInputSnapshot Capture(DescriptorReviewReportBuildRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.VisibilityApplied)
            throw new InvalidOperationException(
                "Cannot capture review report inputs: visibility has not been applied to the review result. " +
                "Apply visibility filtering before building the report.");

        ArgumentNullException.ThrowIfNull(request.ReviewResult);
        ArgumentNullException.ThrowIfNull(request.Draft);
        var review = request.ReviewResult;
        var draft = request.Draft;
        ArgumentNullException.ThrowIfNull(review.ValidationResult);
        ArgumentNullException.ThrowIfNull(review.Diagnostics);
        ValidateIdentity(review.TenantId, review.DraftId, draft.TenantId, draft.DraftId);

        DescriptorReviewReportMaterializationInput? materialization = null;
        if (review.MaterializationResult is { } sourceMaterialization)
        {
            ArgumentNullException.ThrowIfNull(sourceMaterialization.ProposedInventory);
            ArgumentNullException.ThrowIfNull(sourceMaterialization.Diagnostics);
            materialization = new DescriptorReviewReportMaterializationInput
            {
                IsMaterialized = sourceMaterialization.IsMaterialized,
                ProposedDescriptors = Array.AsReadOnly(sourceMaterialization.ProposedInventory
                    .Select(descriptor =>
                    {
                        ArgumentNullException.ThrowIfNull(descriptor);
                        return new DescriptorReviewReportDescriptorInput
                        {
                            Namespace = descriptor.Namespace,
                            Id = descriptor.Id,
                            Name = descriptor.Name,
                            Kind = descriptor.Kind,
                        };
                    }).ToArray()),
                Diagnostics = CopyDiagnostics(sourceMaterialization.Diagnostics),
            };
        }

        DescriptorReviewReportTopologyInput? topology = null;
        if (review.TopologySnapshot is { } sourceTopology)
        {
            topology = new DescriptorReviewReportTopologyInput
            {
                NodeCount = sourceTopology.NodeCount,
                EdgeCount = sourceTopology.EdgeCount,
                NodeCountsByKind = Array.AsReadOnly(sourceTopology.Nodes.Values
                    .GroupBy(node => node.Kind)
                    .OrderBy(group => group.Key)
                    .Select(group => new DescriptorReviewReportKindCountInput { Kind = group.Key, Count = group.Count() })
                    .ToArray()),
                EdgeCountsByKind = Array.AsReadOnly(sourceTopology.Edges
                    .GroupBy(edge => edge.Kind)
                    .OrderBy(group => group.Key)
                    .Select(group => new DescriptorReviewReportRelationshipCountInput { Kind = group.Key, Count = group.Count() })
                    .ToArray()),
            };
        }

        DescriptorReviewReportImpactInput? impact = null;
        if (review.ImpactAnalysisResult is { } sourceImpact)
        {
            ArgumentNullException.ThrowIfNull(sourceImpact.AffectedDescriptors);
            impact = new DescriptorReviewReportImpactInput
            {
                MaxSeverity = sourceImpact.MaxSeverity,
                AffectedDescriptors = Array.AsReadOnly(sourceImpact.AffectedDescriptors.Select(affected =>
                    new DescriptorReviewReportAffectedInput
                    {
                        Name = affected.Name,
                        Kind = affected.Kind,
                        Severity = affected.Severity,
                        Reason = affected.Reason,
                    }).ToArray()),
            };
        }

        IReadOnlyList<DescriptorReviewReportCompatibilityFindingInput>? compatibility = null;
        if (review.CompatibilityResult is { } sourceCompatibility)
        {
            ArgumentNullException.ThrowIfNull(sourceCompatibility.Findings);
            compatibility = Array.AsReadOnly(sourceCompatibility.Findings.Select(finding =>
                new DescriptorReviewReportCompatibilityFindingInput
                {
                    Level = finding.Level,
                    SubjectId = finding.Subject.Id,
                }).ToArray());
        }

        DescriptorLifecycleDecisionKind? firstDecision = null;
        DescriptorLifecycleTransition? firstTransition = null;
        IReadOnlyList<string>? packageFindingIds = null;
        if (review.GovernanceDecision is { } governance)
        {
            ArgumentNullException.ThrowIfNull(governance.Decisions);
            ArgumentNullException.ThrowIfNull(governance.PackageFindings);
            var first = governance.Decisions.FirstOrDefault();
            firstDecision = first?.Decision;
            firstTransition = first?.Transition;
            packageFindingIds = Array.AsReadOnly(governance.PackageFindings
                .Select(finding => finding.Subject?.Id ?? string.Empty).ToArray());
        }

        DescriptorReviewReportPackagePreviewInput? package = null;
        if (review.PackagePreview is { } sourcePackage)
        {
            ArgumentNullException.ThrowIfNull(sourcePackage.DescriptorIds);
            package = new DescriptorReviewReportPackagePreviewInput
            {
                DescriptorIds = Array.AsReadOnly(sourcePackage.DescriptorIds.ToArray()),
                ManifestHash = sourcePackage.PackageManifestHash,
                EvidenceHash = sourcePackage.PackageEvidenceHash,
                EnvelopeHash = sourcePackage.PackageEvidenceEnvelopeHash,
            };
        }

        var snapshot = new DescriptorReviewReportInputSnapshot
        {
            Version = CurrentVersion,
            TenantId = review.TenantId,
            DraftId = review.DraftId,
            IsActivationEligible = review.IsActivationEligible,
            IsValid = review.ValidationResult.IsValid,
            ValidationDiagnostics = CopyDiagnostics(review.ValidationResult.Diagnostics),
            ReviewDiagnostics = CopyDiagnostics(review.Diagnostics),
            Owner = new DescriptorReviewReportOwnerInput
            {
                TenantId = draft.TenantId,
                DraftId = draft.DraftId,
                DescriptorId = draft.DescriptorId,
                DescriptorKind = draft.DescriptorKind,
                Operation = draft.Operation,
                AuthorKind = draft.AuthorKind,
                AuthorId = draft.AuthorId,
                Status = draft.Status,
                ProposedVersion = draft.ProposedVersion,
                BaseVersion = draft.BaseVersion,
            },
            Materialization = materialization,
            Topology = topology,
            Impact = impact,
            CompatibilityFindings = compatibility,
            GovernanceMaxDecision = review.GovernanceDecision?.MaxDecision,
            GovernanceFirstDecision = firstDecision,
            GovernanceFirstTransition = firstTransition,
            GovernancePackageFindingSubjectIds = packageFindingIds,
            PackagePreview = package,
            StableHashes = review.StableHashes,
        };
        snapshot.Validate();
        return snapshot;
    }

    /// <summary>Validates serialized structure before it is rendered or hashed.</summary>
    public void Validate()
    {
        if (Version != CurrentVersion)
            throw new NotSupportedException($"Review report input version '{Version}' is not supported.");
        if (string.IsNullOrWhiteSpace(TenantId) || string.IsNullOrWhiteSpace(DraftId))
            throw new ArgumentException("Review report input TenantId and DraftId are required.");
        ArgumentNullException.ThrowIfNull(Owner);
        ArgumentNullException.ThrowIfNull(ValidationDiagnostics);
        ArgumentNullException.ThrowIfNull(ReviewDiagnostics);
        ValidateIdentity(TenantId, DraftId, Owner.TenantId, Owner.DraftId);
        if (Owner.DescriptorId is null || Owner.AuthorId is null)
            throw new ArgumentException("Review report owner facts are malformed.");
        ValidateDiagnostics(ValidationDiagnostics);
        ValidateDiagnostics(ReviewDiagnostics);
        if (Materialization is { } materialization)
        {
            ArgumentNullException.ThrowIfNull(materialization.ProposedDescriptors);
            ArgumentNullException.ThrowIfNull(materialization.Diagnostics);
            ValidateDiagnostics(materialization.Diagnostics);
            foreach (var descriptor in materialization.ProposedDescriptors)
                if (descriptor is null || descriptor.Namespace is null || descriptor.Id is null || descriptor.Name is null)
                    throw new ArgumentException("Review report materialization descriptor facts are malformed.");
        }
        if (Topology is { } topology)
        {
            ArgumentNullException.ThrowIfNull(topology.NodeCountsByKind);
            ArgumentNullException.ThrowIfNull(topology.EdgeCountsByKind);
            if (topology.NodeCount < 0 || topology.EdgeCount < 0 ||
                topology.NodeCountsByKind.Any(item => item is null || item.Count < 0) ||
                topology.EdgeCountsByKind.Any(item => item is null || item.Count < 0) ||
                topology.NodeCountsByKind.Select(item => item.Kind).Distinct().Count() != topology.NodeCountsByKind.Count ||
                topology.EdgeCountsByKind.Select(item => item.Kind).Distinct().Count() != topology.EdgeCountsByKind.Count ||
                topology.NodeCountsByKind.Sum(item => item.Count) != topology.NodeCount ||
                topology.EdgeCountsByKind.Sum(item => item.Count) != topology.EdgeCount)
                throw new ArgumentException("Review report topology facts are malformed.");
        }
        if (Impact is { } impact)
        {
            ArgumentNullException.ThrowIfNull(impact.AffectedDescriptors);
            if (impact.AffectedDescriptors.Any(item => item is null || item.Name is null))
                throw new ArgumentException("Review report impact facts are malformed.");
        }
        if (CompatibilityFindings?.Any(item => item is null || item.SubjectId is null) == true)
            throw new ArgumentException("Review report compatibility facts are malformed.");
        if (GovernancePackageFindingSubjectIds?.Any(item => item is null) == true)
            throw new ArgumentException("Review report governance facts are malformed.");
        if (GovernanceMaxDecision is null)
        {
            if (GovernanceFirstDecision is not null || GovernanceFirstTransition is not null || GovernancePackageFindingSubjectIds is not null)
                throw new ArgumentException("Review report governance facts are inconsistent.");
        }
        else if (GovernancePackageFindingSubjectIds is null ||
                 (GovernanceFirstDecision is null) != (GovernanceFirstTransition is null))
        {
            throw new ArgumentException("Review report governance facts are inconsistent.");
        }
        if (PackagePreview is { } package && package.DescriptorIds is null)
            throw new ArgumentException("Review report package preview facts are malformed.");
    }

    /// <summary>Re-derives the canonical hash projection from captured report facts.</summary>
    public DescriptorDraftReviewHashInput ToReviewHashInput()
    {
        Validate();
        return new DescriptorDraftReviewHashInput
        {
            Version = DescriptorDraftReviewHashInput.CurrentVersion,
            SourceBinding = new ReviewResultSourceBindingProjection
            {
                TenantId = TenantId,
                DraftId = DraftId,
                IsActivationEligible = IsActivationEligible,
                IsValid = IsValid,
                Diagnostics = Array.AsReadOnly(ReviewDiagnostics.Select(diagnostic => new ReviewDiagnosticProjection
                {
                    Code = diagnostic.Code.ToString(),
                    Severity = diagnostic.Severity.ToString(),
                }).ToArray()),
                GovernanceDecision = GovernanceMaxDecision?.ToString(),
                ImpactSeverity = Impact?.MaxSeverity.ToString(),
            },
        };
    }

    private static IReadOnlyList<DescriptorDraftDiagnostic> CopyDiagnostics(IReadOnlyList<DescriptorDraftDiagnostic> source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var copy = source.Select(diagnostic =>
        {
            ArgumentNullException.ThrowIfNull(diagnostic);
            return diagnostic with { };
        }).ToArray();
        return Array.AsReadOnly(copy);
    }

    private static void ValidateDiagnostics(IReadOnlyList<DescriptorDraftDiagnostic> diagnostics)
    {
        foreach (var diagnostic in diagnostics)
            if (diagnostic is null || diagnostic.Message is null)
                throw new ArgumentException("Review report diagnostics must contain valid diagnostic records.");
    }

    private static void ValidateIdentity(string tenantId, string draftId, string ownerTenantId, string ownerDraftId)
    {
        if (string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(draftId) ||
            !StringComparer.Ordinal.Equals(tenantId, ownerTenantId) ||
            !StringComparer.Ordinal.Equals(draftId, ownerDraftId))
            throw new ArgumentException("Review report owner identity must match the review result identity.");
    }
}

public sealed record DescriptorReviewReportOwnerInput
{
    public required string TenantId { get; init; }
    public required string DraftId { get; init; }
    public required string DescriptorId { get; init; }
    public required DescriptorKind DescriptorKind { get; init; }
    public required DescriptorDraftOperation Operation { get; init; }
    public required DescriptorDraftAuthorKind AuthorKind { get; init; }
    public required string AuthorId { get; init; }
    public required DescriptorDraftStatus Status { get; init; }
    public string? ProposedVersion { get; init; }
    public string? BaseVersion { get; init; }
}

public sealed record DescriptorReviewReportMaterializationInput
{
    public required bool IsMaterialized { get; init; }
    public required IReadOnlyList<DescriptorReviewReportDescriptorInput> ProposedDescriptors { get; init; }
    public required IReadOnlyList<DescriptorDraftDiagnostic> Diagnostics { get; init; }
}

public sealed record DescriptorReviewReportDescriptorInput
{
    public required string Namespace { get; init; }
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required DescriptorKind Kind { get; init; }
}

public sealed record DescriptorReviewReportTopologyInput
{
    public required int NodeCount { get; init; }
    public required int EdgeCount { get; init; }
    public required IReadOnlyList<DescriptorReviewReportKindCountInput> NodeCountsByKind { get; init; }
    public required IReadOnlyList<DescriptorReviewReportRelationshipCountInput> EdgeCountsByKind { get; init; }
}

public sealed record DescriptorReviewReportKindCountInput
{
    public required DescriptorKind Kind { get; init; }
    public required int Count { get; init; }
}

public sealed record DescriptorReviewReportRelationshipCountInput
{
    public required RelationshipKind Kind { get; init; }
    public required int Count { get; init; }
}

public sealed record DescriptorReviewReportImpactInput
{
    public required DescriptorImpactSeverity MaxSeverity { get; init; }
    public required IReadOnlyList<DescriptorReviewReportAffectedInput> AffectedDescriptors { get; init; }
}

public sealed record DescriptorReviewReportAffectedInput
{
    public required string Name { get; init; }
    public required DescriptorKind Kind { get; init; }
    public required DescriptorImpactSeverity Severity { get; init; }
    public string? Reason { get; init; }
}

public sealed record DescriptorReviewReportCompatibilityFindingInput
{
    public required DescriptorCompatibilityLevel Level { get; init; }
    public required string SubjectId { get; init; }
}

public sealed record DescriptorReviewReportPackagePreviewInput
{
    public required IReadOnlyList<string> DescriptorIds { get; init; }
    public CanonicalHash? ManifestHash { get; init; }
    public CanonicalHash? EvidenceHash { get; init; }
    public CanonicalHash? EnvelopeHash { get; init; }
}
