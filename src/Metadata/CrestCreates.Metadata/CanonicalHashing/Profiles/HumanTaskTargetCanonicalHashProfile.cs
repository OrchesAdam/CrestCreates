using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Workflow.Abstractions;

namespace CrestCreates.Metadata.CanonicalHashing.Profiles;

/// <summary>
/// Canonical hash profile for <see cref="HumanTaskTarget"/>.
/// Case profile used by <see cref="InteractionTargetCanonicalHashProfile"/> union.
///
/// Contract field: <see cref="HumanTaskTarget.HumanTask"/> (VersionedDescriptorRef&lt;HumanTaskDescriptor&gt;)
/// with value profile <see cref="VersionedDescriptorRefHumanTaskCanonicalHashProfile"/>.
/// </summary>
[CanonicalHashProfile(
    ArtifactKind = CanonicalHashArtifactKind.Descriptor,
    DescriptorKind = DescriptorKind.Unknown,
    TargetType = typeof(HumanTaskTarget),
    ContractShapeVersion = "humantask-target-contract-hash-v1",
    DefinitionShapeVersion = "humantask-target-definition-hash-v1")]
internal sealed class HumanTaskTargetCanonicalHashProfile
{
    [CanonicalHashField(nameof(HumanTaskTarget.HumanTask), CanonicalHashFieldClassification.Contract, Order = 0,
        ValueProfile = typeof(VersionedDescriptorRefHumanTaskCanonicalHashProfile))]
    private static void Fields() { }
}
