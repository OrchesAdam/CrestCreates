namespace CrestCreates.Metadata.Abstractions.DescriptorPackage;

/// <summary>
/// Computes canonical hashes for package manifest, evidence, and evidence envelope
/// as an atomic DescriptorPackageHashSet.
/// </summary>
public interface IDescriptorPackageCanonicalHashComputer
{
    DescriptorPackageHashSet ComputeHashSet(
        DescriptorManifest manifest,
        DescriptorPackageEvidence evidence,
        DescriptorPackageEvidenceEnvelopeMetadata envelopeMetadata);
}
