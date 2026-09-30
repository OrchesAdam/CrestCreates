using System.Text.Json;
using CrestCreates.Agent.ControlPlane.Abstractions.Json;
using CrestCreates.Agent.ControlPlane.Abstractions.PackageArtifacts;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Metadata.Abstractions.DescriptorPackage;
using DraftPackagePreview = CrestCreates.DescriptorDraft.Abstractions.DescriptorPackagePreview;

namespace CrestCreates.Agent.ControlPlane.PackageArtifacts;

/// <summary>Checks retained content using the official package hashes and a separate storage-integrity profile.</summary>
public sealed class AgentPackageArtifactValidator : IAgentPackageArtifactValidator
{
    private const string IntegrityArtifactKind = "AgentPackageArtifactContent";
    private const string IntegrityShapeVersion = "agent-package-artifact-content-v1";
    private readonly IDescriptorPackageSerializer _serializer;
    private readonly IDescriptorPackageCanonicalHashComputer _packageHashComputer;
    private readonly ICanonicalHashComputer _hashComputer;

    public AgentPackageArtifactValidator(
        IDescriptorPackageSerializer serializer,
        IDescriptorPackageCanonicalHashComputer packageHashComputer,
        ICanonicalHashComputer hashComputer)
    {
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _packageHashComputer = packageHashComputer ?? throw new ArgumentNullException(nameof(packageHashComputer));
        _hashComputer = hashComputer ?? throw new ArgumentNullException(nameof(hashComputer));
    }

    public void ValidatePackage(AgentPackageArtifactEnvelope package)
    {
        ArgumentNullException.ThrowIfNull(package);
        package.ValidateBinding();
        var content = _serializer.Deserialize(package.PackageJson);
        if (content.Hashes is null || content.EvidenceEnvelope is null)
            throw new InvalidOperationException("Package artifact is missing its canonical hash set or evidence envelope.");

        var manifest = content.Manifest;
        var envelope = content.EvidenceEnvelope;
        if (!StringComparer.Ordinal.Equals(manifest.PackageId, envelope.PackageId) ||
            !StringComparer.Ordinal.Equals(manifest.PackageVersion, envelope.PackageVersion) ||
            manifest.CreatedAt != envelope.CreatedAt ||
            !StringComparer.Ordinal.Equals(manifest.CreatedBy, envelope.CreatedBy) ||
            !StringComparer.Ordinal.Equals(manifest.Source, envelope.Source))
            throw new InvalidOperationException("Package manifest and evidence envelope identities differ.");
        if (!StringComparer.Ordinal.Equals(manifest.PackageId, package.Owner.DraftId) ||
            !StringComparer.Ordinal.Equals(manifest.PackageVersion, package.Owner.ProposedVersion ?? "1") ||
            !StringComparer.Ordinal.Equals(envelope.CreatedBy, package.Owner.AuthorId) ||
            manifest.CreatedAt != package.PackageCreatedAt)
            throw new InvalidOperationException("Package identity, version, author, or creation time does not match its captured owner.");

        var expectedHashes = _packageHashComputer.ComputeHashSet(manifest, content.Evidence, new DescriptorPackageEvidenceEnvelopeMetadata
        {
            PackageId = envelope.PackageId,
            PackageVersion = envelope.PackageVersion,
            CreatedAt = envelope.CreatedAt,
            CreatedBy = envelope.CreatedBy,
            Source = envelope.Source
        });
        if (content.Hashes != expectedHashes ||
            envelope.PackageManifestHash != expectedHashes.PackageManifestHash ||
            envelope.PackageEvidenceHash != expectedHashes.PackageEvidenceHash ||
            package.ProjectedPreview.PackageManifestHash != expectedHashes.PackageManifestHash ||
            package.ProjectedPreview.PackageEvidenceHash != expectedHashes.PackageEvidenceHash ||
            package.ProjectedPreview.PackageEvidenceEnvelopeHash != expectedHashes.PackageEvidenceEnvelopeHash)
            throw new InvalidOperationException("Package artifact canonical hashes do not match the retained package content.");

        if (!StringComparer.Ordinal.Equals(content.SnapshotData.PackageId, manifest.PackageId) ||
            !StringComparer.Ordinal.Equals(content.SnapshotData.PackageVersion, manifest.PackageVersion) ||
            !StringComparer.Ordinal.Equals(content.SnapshotData.SnapshotId, $"snapshot_{expectedHashes.PackageManifestHash.Value[..16]}") ||
            content.SnapshotData.Descriptors.Count != manifest.DescriptorEntries.Count)
            throw new InvalidOperationException("Package snapshot identity or descriptor inventory is inconsistent with its manifest.");

        var manifestEntries = manifest.DescriptorEntries.OrderBy(e => e.Kind).ThenBy(e => e.Ref.Id, StringComparer.Ordinal).ToArray();
        var snapshotEntries = content.SnapshotData.Descriptors.OrderBy(e => e.Kind).ThenBy(e => e.Ref.Id, StringComparer.Ordinal).ToArray();
        for (var index = 0; index < manifestEntries.Length; index++)
        {
            var manifestEntry = manifestEntries[index];
            var snapshotEntry = snapshotEntries[index];
            if (manifestEntry.Ref != snapshotEntry.Ref || manifestEntry.Kind != snapshotEntry.Kind ||
                manifestEntry.State != snapshotEntry.State ||
                !StringComparer.Ordinal.Equals(manifestEntry.Name, snapshotEntry.DescriptorName) ||
                !StringComparer.Ordinal.Equals(manifestEntry.ContractHash, snapshotEntry.ContractHash) ||
                !StringComparer.Ordinal.Equals(manifestEntry.DefinitionHash, snapshotEntry.DefinitionHash) ||
                !StringComparer.Ordinal.Equals(manifestEntry.SupersededById, snapshotEntry.SupersededById))
                throw new InvalidOperationException("Package snapshot entries differ from the canonical manifest inventory.");
        }

        var manifestIds = manifest.DescriptorEntries.Select(e => e.Ref.Id).ToHashSet(StringComparer.Ordinal);
        if (package.ProjectedPreview.DescriptorIds.Any(id => !manifestIds.Contains(id)) ||
            package.ProjectedPreview.DescriptorIds.Distinct(StringComparer.Ordinal).Count() != package.ProjectedPreview.DescriptorIds.Count)
            throw new InvalidOperationException("Package preview contains descriptor IDs outside the retained package manifest.");

        var expectedIntegrity = ComputePackageIntegrity(package);
        if (package.ContentIntegrityHash != expectedIntegrity)
            throw new InvalidOperationException("Package artifact content integrity hash does not match its retained envelope and exact JSON content.");
    }

    public void ValidateEvidence(AgentEvidenceArtifactEnvelope evidence, AgentPackageArtifactEnvelope package)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(package);
        evidence.ValidateBinding();
        ValidatePackage(package);
        package.ValidateBinding();
        if (!StringComparer.Ordinal.Equals(evidence.TenantId, package.TenantId) ||
            !StringComparer.Ordinal.Equals(evidence.PackagePreviewId, package.PackagePreviewId) ||
            !StringComparer.Ordinal.Equals(evidence.Owner.DraftId, package.Owner.DraftId) ||
            evidence.Owner != package.Owner ||
            !StringComparer.Ordinal.Equals(evidence.ScopeFingerprint, package.ScopeFingerprint) ||
            !StringComparer.Ordinal.Equals(evidence.DraftVersion, package.DraftVersion))
            throw new InvalidOperationException("Evidence artifact does not reference its exact package owner, scope, and draft version.");

        if (!StringComparer.Ordinal.Equals(evidence.ProjectedEvidence.DraftId, package.Owner.DraftId) ||
            !PackagePreviewsEqual(evidence.ProjectedEvidence.PackagePreview, package.ProjectedPreview))
            throw new InvalidOperationException("Evidence projection package preview does not match its exact parent package preview.");

        var expected = ComputeEvidenceIntegrity(evidence);
        if (evidence.ContentIntegrityHash != expected)
            throw new InvalidOperationException("Evidence artifact content integrity hash does not match its retained envelope.");
    }

    internal CanonicalHash ComputePackageIntegrity(AgentPackageArtifactEnvelope artifact)
    {
        var projectionJson = JsonSerializer.Serialize(artifact.ProjectedPreview, AgentPackageArtifactProjectionJsonSerializerContext.Default.DescriptorPackagePreview);
        return _hashComputer.ComputeFromProjection(CanonicalHashProjectionResult.Create(CreateMetadata("package"), writer =>
        {
            writer.WriteStartObject();
            WriteIdentity(writer, artifact.Version, artifact.TenantId, artifact.PackagePreviewId, artifact.CapturedAt, artifact.Owner, artifact.ScopeFingerprint, artifact.DraftVersion);
            writer.WriteString("visibleCatalogFingerprint", artifact.VisibleCatalogFingerprint);
            writer.WriteString("packageCreatedAt", artifact.PackageCreatedAt);
            writer.WriteString("packageJson", artifact.PackageJson);
            writer.WriteString("projectedPreviewJson", projectionJson);
            writer.WriteEndObject();
        }));
    }

    internal CanonicalHash ComputeEvidenceIntegrity(AgentEvidenceArtifactEnvelope artifact)
    {
        var projectionJson = JsonSerializer.Serialize(artifact.ProjectedEvidence, AgentPackageArtifactProjectionJsonSerializerContext.Default.PackageEvidencePreview);
        return _hashComputer.ComputeFromProjection(CanonicalHashProjectionResult.Create(CreateMetadata("evidence"), writer =>
        {
            writer.WriteStartObject();
            WriteIdentity(writer, artifact.Version, artifact.TenantId, artifact.EvidencePreviewId, artifact.CapturedAt, artifact.Owner, artifact.ScopeFingerprint, artifact.DraftVersion);
            writer.WriteString("packagePreviewId", artifact.PackagePreviewId);
            writer.WriteString("projectedEvidenceJson", projectionJson);
            writer.WriteEndObject();
        }));
    }

    private static void WriteIdentity(Utf8JsonWriter writer, int version, string tenantId, string artifactId, DateTimeOffset capturedAt,
        AgentPackageArtifactOwner owner, string scopeFingerprint, string draftVersion)
    {
        writer.WriteNumber("envelopeVersion", version);
        writer.WriteString("tenantId", tenantId);
        writer.WriteString("artifactId", artifactId);
        writer.WriteString("capturedAt", capturedAt);
        writer.WriteString("scopeFingerprint", scopeFingerprint);
        writer.WriteString("draftVersion", draftVersion);
        writer.WriteStartObject("owner");
        writer.WriteString("tenantId", owner.TenantId);
        writer.WriteString("draftId", owner.DraftId);
        writer.WriteString("descriptorId", owner.DescriptorId);
        writer.WriteString("descriptorKind", owner.DescriptorKind.ToString());
        writer.WriteString("operation", owner.Operation.ToString());
        writer.WriteString("authorKind", owner.AuthorKind.ToString());
        writer.WriteString("authorId", owner.AuthorId);
        writer.WriteString("status", owner.Status.ToString());
        WriteNullable(writer, "baseVersion", owner.BaseVersion);
        WriteNullable(writer, "proposedVersion", owner.ProposedVersion);
        writer.WriteEndObject();
    }

    private static CanonicalHashMetadata CreateMetadata(string variant) => new()
    {
        ArtifactKind = IntegrityArtifactKind,
        Purpose = CanonicalHashPurposeNames.Integrity,
        Scope = CanonicalHashScopeNames.InternalFull,
        AlgorithmVersion = "sha256-canonical-json-v1",
        ContractVersion = CanonicalHashContractVersions.DescriptorHash,
        CanonicalShapeVersion = $"{IntegrityShapeVersion}-{variant}"
    };

    private static void WriteNullable(Utf8JsonWriter writer, string name, string? value)
    {
        if (value is null) writer.WriteNull(name); else writer.WriteString(name, value);
    }

    private static bool PackagePreviewsEqual(DraftPackagePreview left, DraftPackagePreview right) =>
        left.PackageManifestHash == right.PackageManifestHash &&
        left.PackageEvidenceHash == right.PackageEvidenceHash &&
        left.PackageEvidenceEnvelopeHash == right.PackageEvidenceEnvelopeHash &&
        left.DescriptorIds.SequenceEqual(right.DescriptorIds, StringComparer.Ordinal);
}
