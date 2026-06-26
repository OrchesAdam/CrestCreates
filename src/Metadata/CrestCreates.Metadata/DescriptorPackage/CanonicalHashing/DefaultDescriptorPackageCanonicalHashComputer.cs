using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Metadata.Abstractions.DescriptorPackage;
using CrestCreates.Metadata.CanonicalHashing;

namespace CrestCreates.Metadata.DescriptorPackage.CanonicalHashing;

/// <summary>
/// Computes all package canonical hashes as an atomic DescriptorPackageHashSet.
/// Uses ICanonicalHashComputer.ComputeFromProjection with dedicated canonical writers.
/// </summary>
public sealed class DefaultDescriptorPackageCanonicalHashComputer : IDescriptorPackageCanonicalHashComputer
{
    private readonly ICanonicalHashComputer _hashComputer;

    public DefaultDescriptorPackageCanonicalHashComputer(ICanonicalHashComputer hashComputer)
        => _hashComputer = hashComputer;

    public DescriptorPackageHashSet ComputeHashSet(
        DescriptorManifest manifest,
        DescriptorPackageEvidence evidence,
        DescriptorPackageEvidenceEnvelopeMetadata envelopeMetadata)
    {
        var packageManifestHash = _hashComputer.ComputeFromProjection(
            CanonicalHashProjectionResult.Create(
                CreateMetadata(CanonicalHashArtifactNames.PackageManifest, CanonicalHashPurposeNames.Integrity, DescriptorPackageCanonicalShapeVersions.PackageManifestV1),
                writer => DescriptorPackageManifestCanonicalHashWriter.WritePayload(writer, manifest)));

        var packageEvidenceHash = _hashComputer.ComputeFromProjection(
            CanonicalHashProjectionResult.Create(
                CreateMetadata(CanonicalHashArtifactNames.PackageEvidence, CanonicalHashPurposeNames.AuditEvidence, DescriptorPackageCanonicalShapeVersions.PackageEvidenceV1),
                writer => DescriptorPackageEvidenceCanonicalHashWriter.WritePayload(writer, evidence)));

        // Build envelope projection with computed hashes for envelope hash
        var envelopeProjection = new DescriptorPackageEvidenceEnvelope
        {
            PackageId = envelopeMetadata.PackageId,
            PackageVersion = envelopeMetadata.PackageVersion,
            CreatedAt = envelopeMetadata.CreatedAt,
            CreatedBy = envelopeMetadata.CreatedBy,
            Source = envelopeMetadata.Source,
            PackageManifestHash = packageManifestHash,
            PackageEvidenceHash = packageEvidenceHash
        };

        var packageEvidenceEnvelopeHash = _hashComputer.ComputeFromProjection(
            CanonicalHashProjectionResult.Create(
                CreateMetadata(CanonicalHashArtifactNames.PackageEvidenceEnvelope, CanonicalHashPurposeNames.AuditEvidence, DescriptorPackageCanonicalShapeVersions.PackageEvidenceEnvelopeV1),
                writer => DescriptorPackageEvidenceEnvelopeCanonicalHashWriter.WritePayload(writer, envelopeProjection)));

        return new DescriptorPackageHashSet
        {
            PackageManifestHash = packageManifestHash,
            PackageEvidenceHash = packageEvidenceHash,
            PackageEvidenceEnvelopeHash = packageEvidenceEnvelopeHash
        };
    }

    private static CanonicalHashMetadata CreateMetadata(string artifactKind, string purpose, string shapeVersion) => new()
    {
        ArtifactKind = artifactKind,
        Purpose = purpose,
        Scope = CanonicalHashScopeNames.InternalFull,
        AlgorithmVersion = "sha256-canonical-json-v1",
        ContractVersion = CanonicalHashContractVersions.DescriptorHash,
        CanonicalShapeVersion = shapeVersion
    };
}
