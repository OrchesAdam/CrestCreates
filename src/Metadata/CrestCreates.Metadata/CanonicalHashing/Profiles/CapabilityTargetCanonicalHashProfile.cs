using CrestCreates.Metadata.Abstractions;
using CrestCreates.Workflow.Abstractions;

namespace CrestCreates.Metadata.CanonicalHashing.Profiles;

/// <summary>
/// Canonical hash profile for <see cref="CapabilityTarget"/>.
/// Case profile used by <see cref="InteractionTargetCanonicalHashProfile"/> union.
///
/// Contract field: <see cref="CapabilityTarget.Capability"/> (VersionedDescriptorRef&lt;IVersionedDescriptor&gt;)
/// with value profile <see cref="VersionedDescriptorRefBaseCanonicalHashProfile"/>.
/// </summary>
[CanonicalHashProfile(
    ArtifactKind = CanonicalHashArtifactKind.Descriptor,
    DescriptorKind = DescriptorKind.Unknown,
    TargetType = typeof(CapabilityTarget),
    ContractShapeVersion = "capability-target-contract-hash-v1",
    DefinitionShapeVersion = "capability-target-definition-hash-v1")]
internal sealed class CapabilityTargetCanonicalHashProfile
{
    [CanonicalHashField(nameof(CapabilityTarget.Capability), CanonicalHashFieldClassification.Contract, Order = 0,
        ValueProfile = typeof(VersionedDescriptorRefBaseCanonicalHashProfile))]
    private static void Fields() { }
}
