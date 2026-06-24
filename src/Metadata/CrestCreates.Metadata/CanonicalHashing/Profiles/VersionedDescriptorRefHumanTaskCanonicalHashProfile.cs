using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Metadata.CanonicalHashing.Profiles;

/// <summary>
/// Canonical hash profile for <see cref="VersionedDescriptorRef{TDescriptor}"/>
/// specialized for <see cref="HumanTaskDescriptor"/>.
///
/// Used by <see cref="HumanTaskTargetCanonicalHashProfile"/> to hash
/// <see cref="HumanTaskTarget.HumanTask"/>.
///
/// Only <c>Id</c> and <c>Version</c> participate in the hash.
/// <c>SelectionMode</c> and <c>ExpectedContractHash</c> are excluded.
/// </summary>
[CanonicalHashProfile(
    ArtifactKind = CanonicalHashArtifactKind.Descriptor,
    DescriptorKind = DescriptorKind.Unknown,
    TargetType = typeof(VersionedDescriptorRef<HumanTaskDescriptor>),
    ContractShapeVersion = "descriptorref-humantask-hash-v1",
    DefinitionShapeVersion = "descriptorref-humantask-hash-v1")]
internal sealed class VersionedDescriptorRefHumanTaskCanonicalHashProfile
{
    [CanonicalHashField(nameof(VersionedDescriptorRef<HumanTaskDescriptor>.Id), CanonicalHashFieldClassification.Contract, Order = 0)]
    [CanonicalHashField(nameof(VersionedDescriptorRef<HumanTaskDescriptor>.Version), CanonicalHashFieldClassification.Contract, Order = 1)]

    [CanonicalHashField(nameof(VersionedDescriptorRef<HumanTaskDescriptor>.SelectionMode), CanonicalHashFieldClassification.Excluded,
        Reason = "Protocol-level resolution concern — not part of structural hash")]
    [CanonicalHashField(nameof(VersionedDescriptorRef<HumanTaskDescriptor>.ExpectedContractHash), CanonicalHashFieldClassification.Excluded,
        Reason = "Resolution-time input — not part of structural hash")]

    private static void Fields() { }
}
