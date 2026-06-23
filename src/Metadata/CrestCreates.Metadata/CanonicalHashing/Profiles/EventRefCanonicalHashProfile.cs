using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Metadata.CanonicalHashing.Profiles;

/// <summary>
/// Canonical hash profile for <see cref="EventRef"/>.
/// EventRef is a sub-structure on CapabilityDescriptor, not a standalone descriptor.
///
/// Only <c>Namespace</c>, <c>Id</c>, and <c>Version</c> participate in the hash.
/// </summary>
[CanonicalHashProfile(
    ArtifactKind = CanonicalHashArtifactKind.Descriptor,
    DescriptorKind = DescriptorKind.Unknown,
    TargetType = typeof(EventRef),
    ContractShapeVersion = "eventref-contract-hash-v1",
    DefinitionShapeVersion = "eventref-definition-hash-v1")]
internal sealed class EventRefCanonicalHashProfile
{
    [CanonicalHashField(nameof(EventRef.Namespace), CanonicalHashFieldClassification.Contract, Order = 0)]
    [CanonicalHashField(nameof(EventRef.Id), CanonicalHashFieldClassification.Contract, Order = 1)]
    [CanonicalHashField(nameof(EventRef.Version), CanonicalHashFieldClassification.Contract, Order = 2)]

    private static void Fields() { }
}
