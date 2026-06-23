using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Metadata.CanonicalHashing.Profiles;

/// <summary>
/// Canonical hash profile for <see cref="VersionedDescriptorRef{TDescriptor}"/>
/// specialized for <see cref="IInteractionDescriptor"/>.
///
/// Only <c>Id</c> and <c>Version</c> participate in the hash (both ContractHash
/// and DefinitionHash). <c>SelectionMode</c> and <c>ExpectedContractHash</c>
/// are protocol-level concerns excluded from hashing.
/// </summary>
[CanonicalHashProfile(
    ArtifactKind = CanonicalHashArtifactKind.Descriptor,
    DescriptorKind = DescriptorKind.Unknown,
    TargetType = typeof(VersionedDescriptorRef<IInteractionDescriptor>),
    ContractShapeVersion = "descriptorref-interaction-hash-v1",
    DefinitionShapeVersion = "descriptorref-interaction-hash-v1")]
internal sealed class VersionedDescriptorRefInteractionCanonicalHashProfile
{
    [CanonicalHashField(nameof(VersionedDescriptorRef<IInteractionDescriptor>.Id), CanonicalHashFieldClassification.Contract, Order = 0)]
    [CanonicalHashField(nameof(VersionedDescriptorRef<IInteractionDescriptor>.Version), CanonicalHashFieldClassification.Contract, Order = 1)]

    [CanonicalHashField(nameof(VersionedDescriptorRef<IInteractionDescriptor>.SelectionMode), CanonicalHashFieldClassification.Excluded,
        Reason = "Protocol-level resolution concern — not part of structural hash")]
    [CanonicalHashField(nameof(VersionedDescriptorRef<IInteractionDescriptor>.ExpectedContractHash), CanonicalHashFieldClassification.Excluded,
        Reason = "Resolution-time input — not part of structural hash")]

    private static void Fields() { }
}
