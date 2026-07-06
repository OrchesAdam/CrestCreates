using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Metadata.CanonicalHashing.Profiles;

[CanonicalHashProfile(
    ArtifactKind = CanonicalHashArtifactKind.Descriptor,
    DescriptorKind = DescriptorKind.Unknown,
    TargetType = typeof(VersionedDescriptorRef<CapabilityDescriptor>),
    ContractShapeVersion = "capability-ref-hash-v1",
    DefinitionShapeVersion = "capability-ref-hash-v1")]
internal sealed class VersionedDescriptorRefCapabilityCanonicalHashProfile
{
    [CanonicalHashField(nameof(VersionedDescriptorRef<CapabilityDescriptor>.Id), CanonicalHashFieldClassification.Contract, Order = 0)]
    [CanonicalHashField(nameof(VersionedDescriptorRef<CapabilityDescriptor>.Version), CanonicalHashFieldClassification.Contract, Order = 1)]
    [CanonicalHashField(nameof(VersionedDescriptorRef<CapabilityDescriptor>.SelectionMode), CanonicalHashFieldClassification.Excluded,
        Reason = "Protocol-level resolution concern — not part of structural hash")]
    [CanonicalHashField(nameof(VersionedDescriptorRef<CapabilityDescriptor>.ExpectedContractHash), CanonicalHashFieldClassification.Excluded,
        Reason = "Resolution-time input — not part of structural hash")]
    private static void Fields() { }
}
