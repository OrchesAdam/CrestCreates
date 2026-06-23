using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Metadata.CanonicalHashing.Profiles;

/// <summary>
/// Canonical hash profile for <see cref="HumanTaskDescriptor"/>.
///
/// Contract fields (in both ContractHash and DefinitionHash):
///   Id, Name, Version, State, SupersededById,
///   Interaction, InputSchema, OutputSchema,
///   AssigneeStrategy, Permissions, Outcomes
///
/// DefinitionOnly fields (only in DefinitionHash):
///   Timeout
/// </summary>
[CanonicalHashProfile(
    ArtifactKind = CanonicalHashArtifactKind.Descriptor,
    DescriptorKind = DescriptorKind.HumanTask,
    TargetType = typeof(HumanTaskDescriptor),
    ContractShapeVersion = "humantask-contract-hash-v1",
    DefinitionShapeVersion = "humantask-definition-hash-v1")]
internal sealed class HumanTaskDescriptorCanonicalHashProfile
{
    // ── Contract fields (common to both ContractHash and DefinitionHash) ──

    [CanonicalHashField(nameof(HumanTaskDescriptor.Id), CanonicalHashFieldClassification.Contract, Order = 0)]
    [CanonicalHashField(nameof(HumanTaskDescriptor.Name), CanonicalHashFieldClassification.Contract, Order = 1)]
    [CanonicalHashField(nameof(HumanTaskDescriptor.Version), CanonicalHashFieldClassification.Contract, Order = 2)]
    [CanonicalHashField(nameof(HumanTaskDescriptor.State), CanonicalHashFieldClassification.Contract, Order = 3)]
    [CanonicalHashField(nameof(HumanTaskDescriptor.SupersededById), CanonicalHashFieldClassification.Contract, Order = 4)]
    [CanonicalHashField(nameof(HumanTaskDescriptor.Interaction), CanonicalHashFieldClassification.Contract, Order = 10,
        ValueProfile = typeof(VersionedDescriptorRefInteractionCanonicalHashProfile))]
    [CanonicalHashField(nameof(HumanTaskDescriptor.InputSchema), CanonicalHashFieldClassification.Contract, Order = 11,
        ValueProfile = typeof(VersionedSchemaRefCanonicalHashProfile))]
    [CanonicalHashField(nameof(HumanTaskDescriptor.OutputSchema), CanonicalHashFieldClassification.Contract, Order = 12,
        ValueProfile = typeof(VersionedSchemaRefCanonicalHashProfile))]
    [CanonicalHashField(nameof(HumanTaskDescriptor.AssigneeStrategy), CanonicalHashFieldClassification.Contract, Order = 20)]
    [CanonicalHashField(nameof(HumanTaskDescriptor.Permissions), CanonicalHashFieldClassification.Contract, Order = 30)]
    [CanonicalHashField(nameof(HumanTaskDescriptor.Outcomes), CanonicalHashFieldClassification.Contract, Order = 40,
        ElementProfile = typeof(CompletionOutcomeCanonicalHashProfile),
        CollectionOrderMode = CanonicalHashCollectionOrderMode.OrdinalByProperty,
        OrderByProperty = "Condition,Capability.Id,Capability.Version")]

    // ── DefinitionOnly fields (only in DefinitionHash) ──

    [CanonicalHashField(nameof(HumanTaskDescriptor.Timeout), CanonicalHashFieldClassification.DefinitionOnly, Order = 110)]

    // ── Excluded fields ──

    [CanonicalHashField(nameof(HumanTaskDescriptor.Namespace), CanonicalHashFieldClassification.Excluded,
        Reason = "Runtime constant — not part of hash")]
    [CanonicalHashField(nameof(HumanTaskDescriptor.Kind), CanonicalHashFieldClassification.Excluded,
        Reason = "Runtime constant — not part of hash")]
    private static void Fields() { }
}
