using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Metadata.CanonicalHashing.Profiles;

/// <summary>
/// Canonical hash profile for <see cref="CompletionOutcome"/>.
/// Sub-structure of <see cref="HumanTaskDescriptor"/>, not a standalone descriptor.
///
/// All fields participate in both ContractHash and DefinitionHash.
/// </summary>
[CanonicalHashProfile(
    ArtifactKind = CanonicalHashArtifactKind.Descriptor,
    DescriptorKind = DescriptorKind.Unknown,
    TargetType = typeof(CompletionOutcome),
    ContractShapeVersion = "completionoutcome-contract-hash-v1",
    DefinitionShapeVersion = "completionoutcome-definition-hash-v1")]
internal sealed class CompletionOutcomeCanonicalHashProfile
{
    [CanonicalHashField(nameof(CompletionOutcome.Condition), CanonicalHashFieldClassification.Contract, Order = 0)]
    [CanonicalHashField(nameof(CompletionOutcome.Capability), CanonicalHashFieldClassification.Contract, Order = 10,
        ValueProfile = typeof(VersionedDescriptorRefBaseCanonicalHashProfile))]

    private static void Fields() { }
}
