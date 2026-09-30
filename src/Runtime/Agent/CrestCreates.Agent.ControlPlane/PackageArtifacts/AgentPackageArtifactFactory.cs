using System.Globalization;
using CrestCreates.Agent.ControlPlane;
using CrestCreates.Agent.ControlPlane.Abstractions;
using CrestCreates.Agent.ControlPlane.Abstractions.PackageArtifacts;
using CrestCreates.Metadata.Abstractions.DescriptorTopology;
using CrestCreates.DescriptorDraft.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorPackage;
using Draft = CrestCreates.DescriptorDraft.Abstractions.DescriptorDraft;
using DraftPackagePreview = CrestCreates.DescriptorDraft.Abstractions.DescriptorPackagePreview;

namespace CrestCreates.Agent.ControlPlane.PackageArtifacts;

/// <summary>
/// Captures immutable package/evidence artifacts after caller authorization and visibility projection.
/// This factory owns serialization and integrity digests; callers cannot supply either value.
/// </summary>
public sealed class AgentPackageArtifactFactory : IAgentPackageArtifactFactory
{
    private readonly IDescriptorPackageSerializer _serializer;
    private readonly AgentPackageArtifactValidator _validator;
    private readonly AgentDraftArtifactVisibilityProjector _artifactProjector;

    public AgentPackageArtifactFactory(
        IDescriptorPackageSerializer serializer,
        AgentPackageArtifactValidator validator,
        IDescriptorTopologyBuilder topologyBuilder)
    {
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _artifactProjector = new AgentDraftArtifactVisibilityProjector(
            new AgentTopologyVisibilityProjector(),
            topologyBuilder ?? throw new ArgumentNullException(nameof(topologyBuilder)));
    }

    public AgentPackageEvidenceArtifactPair CreateProjectedPair(
        string packagePreviewId,
        string evidencePreviewId,
        DateTimeOffset capturedAt,
        Draft owner,
        AgentToolAuthorizationOptions authorizationOptions,
        IReadOnlyList<IDescriptor> exactCatalogInventory,
        DescriptorPackage package,
        IReadOnlyList<AgentToolDiagnostic>? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(authorizationOptions);
        ArgumentNullException.ThrowIfNull(exactCatalogInventory);
        ArgumentNullException.ThrowIfNull(package);
        var projector = _artifactProjector;
        var scopeFingerprint = ComputeVisibilityScopeFingerprint(authorizationOptions);
        var scope = new AgentDescriptorVisibilityScope(
            owner.TenantId,
            new AgentDescriptorKindPolicyEvaluator(authorizationOptions),
            scopeFingerprint);
        if (!scope.IsVisible(owner.DescriptorKind))
            throw new InvalidOperationException("The package owner descriptor kind is not visible under the capture scope.");

        var universeResult = AgentVisibleDescriptorUniverse.TryCreate(exactCatalogInventory, scope);
        if (!universeResult.IsSuccess)
            throw new InvalidOperationException("The exact package capture inventory contains an invalid descriptor kind.");
        var universe = universeResult.Universe!;

        // The retained package JSON is complete canonical content, so it must itself
        // be inside the authorization scope before a filtered preview can be captured.
        if (package.Manifest.DescriptorEntries.Any(entry => !scope.IsVisible(entry.Kind)))
            throw new InvalidOperationException("The package contains descriptors outside the capture visibility scope.");

        var preview = new DraftPackagePreview
        {
            PackageManifestHash = package.Hashes?.PackageManifestHash,
            PackageEvidenceHash = package.Hashes?.PackageEvidenceHash,
            PackageEvidenceEnvelopeHash = package.Hashes?.PackageEvidenceEnvelopeHash,
            DescriptorIds = package.Manifest.DescriptorEntries.Select(entry => entry.Ref.Id).ToList().AsReadOnly()
        };
        var projectedPreview = projector.ProjectPackage(preview, universe);
        if (projectedPreview is null)
            throw new InvalidOperationException("The package preview cannot be projected unambiguously through the capture scope.");

        var visibleCatalogFingerprint = ComputeVisibleCatalogFingerprint(universe.VisibleDescriptors);
        var packageArtifact = CreatePackage(
            packagePreviewId,
            capturedAt,
            owner,
            scopeFingerprint,
            visibleCatalogFingerprint,
            projectedPreview,
            package);
        var projectedEvidence = projector.ProjectEvidence(new PackageEvidencePreview
        {
            DraftId = owner.DraftId,
            TenantId = owner.TenantId,
            PackagePreview = projectedPreview,
            Evidence = package.Evidence,
            Diagnostics = diagnostics ?? Array.Empty<AgentToolDiagnostic>()
        }, universe);
        var evidenceArtifact = CreateEvidence(evidencePreviewId, capturedAt, packageArtifact, projectedEvidence);
        return new AgentPackageEvidenceArtifactPair { Package = packageArtifact, Evidence = evidenceArtifact };
    }

    /// <summary>Returns the same deterministic visible-catalog fingerprint used by package reuse.</summary>
    public static string ComputeVisibleCatalogFingerprint(IReadOnlyList<IDescriptor> visibleDescriptors)
    {
        ArgumentNullException.ThrowIfNull(visibleDescriptors);
        if (visibleDescriptors.Count == 0)
            return "empty";

        var entries = visibleDescriptors.Select(descriptor =>
        {
            var fullId = descriptor.FullId;
            var kind = ((int)descriptor.Kind).ToString(CultureInfo.InvariantCulture);
            var version = descriptor is IVersionedDescriptor versioned
                ? versioned.Version.ToString(CultureInfo.InvariantCulture)
                : string.Empty;
            return $"{fullId.Length}:{fullId}{kind.Length}:{kind}{version.Length}:{version}";
        }).Order(StringComparer.Ordinal).ToArray();
        return string.Join("|", entries);
    }

    public static string ComputeVisibilityScopeFingerprint(AgentToolAuthorizationOptions authorizationOptions) =>
        AgentDescriptorVisibilityScope.ComputeFingerprint(authorizationOptions);

    public static string ComputeVisibleCatalogFingerprint(
        string tenantId,
        IReadOnlyList<IDescriptor> tenantCatalog,
        AgentToolAuthorizationOptions authorizationOptions)
    {
        var scope = new AgentDescriptorVisibilityScope(
            tenantId,
            new AgentDescriptorKindPolicyEvaluator(authorizationOptions),
            ComputeVisibilityScopeFingerprint(authorizationOptions));
        return ComputeVisibleCatalogFingerprint(scope.Filter(tenantCatalog, descriptor => descriptor.Kind));
    }

    public AgentPackageArtifactEnvelope CreatePackage(
        string packagePreviewId,
        DateTimeOffset capturedAt,
        Draft owner,
        string scopeFingerprint,
        string visibleCatalogFingerprint,
        DescriptorPackagePreview projectedPreview,
        DescriptorPackage package)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(projectedPreview);
        ArgumentNullException.ThrowIfNull(package);
        var envelope = new AgentPackageArtifactEnvelope
        {
            Version = AgentPackageArtifactEnvelope.CurrentVersion,
            TenantId = owner.TenantId,
            PackagePreviewId = packagePreviewId,
            CapturedAt = capturedAt,
            PackageCreatedAt = package.Manifest.CreatedAt,
            Owner = CaptureOwner(owner),
            ScopeFingerprint = scopeFingerprint,
            DraftVersion = owner.ProposedVersion,
            VisibleCatalogFingerprint = visibleCatalogFingerprint,
            ProjectedPreview = projectedPreview,
            PackageJson = _serializer.Serialize(package),
            ContentIntegrityHash = null!
        };
        envelope = envelope with { ContentIntegrityHash = _validator.ComputePackageIntegrity(envelope) };
        _validator.ValidatePackage(envelope);
        return envelope;
    }

    public AgentEvidenceArtifactEnvelope CreateEvidence(
        string evidencePreviewId,
        DateTimeOffset capturedAt,
        AgentPackageArtifactEnvelope package,
        CrestCreates.Agent.ControlPlane.Abstractions.PackageEvidencePreview projectedEvidence)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(projectedEvidence);
        var projected = projectedEvidence with
        {
            PackagePreviewId = package.PackagePreviewId,
            EvidencePreviewId = evidencePreviewId,
            PackagePreview = package.ProjectedPreview
        };
        var envelope = new AgentEvidenceArtifactEnvelope
        {
            Version = AgentEvidenceArtifactEnvelope.CurrentVersion,
            TenantId = package.TenantId,
            EvidencePreviewId = evidencePreviewId,
            CapturedAt = capturedAt,
            PackagePreviewId = package.PackagePreviewId,
            Owner = package.Owner,
            ScopeFingerprint = package.ScopeFingerprint,
            DraftVersion = package.DraftVersion,
            ProjectedEvidence = projected,
            ContentIntegrityHash = null!
        };
        envelope = envelope with { ContentIntegrityHash = _validator.ComputeEvidenceIntegrity(envelope) };
        _validator.ValidateEvidence(envelope, package);
        return envelope;
    }

    public DescriptorPackage ReadPackageContent(AgentPackageArtifactEnvelope package)
    {
        ArgumentNullException.ThrowIfNull(package);
        _validator.ValidatePackage(package);
        return _serializer.Deserialize(package.PackageJson);
    }

    private static AgentPackageArtifactOwner CaptureOwner(Draft owner) => new()
    {
        TenantId = owner.TenantId,
        DraftId = owner.DraftId,
        DescriptorId = owner.DescriptorId,
        DescriptorKind = owner.DescriptorKind,
        Operation = owner.Operation,
        AuthorKind = owner.AuthorKind,
        AuthorId = owner.AuthorId,
        Status = owner.Status,
        BaseVersion = owner.BaseVersion,
        ProposedVersion = owner.ProposedVersion
    };
}
