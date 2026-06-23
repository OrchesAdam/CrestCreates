using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Metadata.CanonicalHashing.Profiles;

/// <summary>
/// Canonical hash profile for <see cref="CapabilityDescriptor"/>.
///
/// Contract fields (in both ContractHash and DefinitionHash):
///   Id, Name, Version, CapabilityKind, State, SupersededById,
///   InputSchema, OutputSchema, Permissions (ordinal by value), RiskLevel
///
/// DefinitionOnly fields (only in DefinitionHash):
///   SemanticTags, Categories, Produces, Consumes
/// </summary>
[CanonicalHashProfile(
    ArtifactKind = CanonicalHashArtifactKind.Descriptor,
    DescriptorKind = DescriptorKind.Capability,
    TargetType = typeof(CapabilityDescriptor),
    ContractShapeVersion = "capability-contract-hash-v1",
    DefinitionShapeVersion = "capability-definition-hash-v1")]
internal sealed class CapabilityDescriptorCanonicalHashProfile
{
    // ── Contract fields (common to both ContractHash and DefinitionHash) ──

    [CanonicalHashField(nameof(CapabilityDescriptor.Id), CanonicalHashFieldClassification.Contract, Order = 0)]
    [CanonicalHashField(nameof(CapabilityDescriptor.Name), CanonicalHashFieldClassification.Contract, Order = 1)]
    [CanonicalHashField(nameof(CapabilityDescriptor.Version), CanonicalHashFieldClassification.Contract, Order = 2)]
    [CanonicalHashField(nameof(CapabilityDescriptor.CapabilityKind), CanonicalHashFieldClassification.Contract, Order = 3)]
    [CanonicalHashField(nameof(CapabilityDescriptor.State), CanonicalHashFieldClassification.Contract, Order = 4)]
    [CanonicalHashField(nameof(CapabilityDescriptor.SupersededById), CanonicalHashFieldClassification.Contract, Order = 5)]
    [CanonicalHashField(nameof(CapabilityDescriptor.InputSchema), CanonicalHashFieldClassification.Contract, Order = 10,
        ValueProfile = typeof(VersionedSchemaRefCanonicalHashProfile))]
    [CanonicalHashField(nameof(CapabilityDescriptor.OutputSchema), CanonicalHashFieldClassification.Contract, Order = 11,
        ValueProfile = typeof(VersionedSchemaRefCanonicalHashProfile))]
    [CanonicalHashField(nameof(CapabilityDescriptor.Permissions), CanonicalHashFieldClassification.Contract, Order = 20,
        CollectionOrderMode = CanonicalHashCollectionOrderMode.OrdinalByValue)]
    [CanonicalHashField(nameof(CapabilityDescriptor.RiskLevel), CanonicalHashFieldClassification.Contract, Order = 30)]

    // ── DefinitionOnly fields (only in DefinitionHash) ──

    [CanonicalHashField(nameof(CapabilityDescriptor.SemanticTags), CanonicalHashFieldClassification.DefinitionOnly, Order = 110,
        CollectionOrderMode = CanonicalHashCollectionOrderMode.OrdinalByValue)]
    [CanonicalHashField(nameof(CapabilityDescriptor.Categories), CanonicalHashFieldClassification.DefinitionOnly, Order = 120,
        CollectionOrderMode = CanonicalHashCollectionOrderMode.OrdinalByValue)]
    [CanonicalHashField(nameof(CapabilityDescriptor.Produces), CanonicalHashFieldClassification.DefinitionOnly, Order = 130,
        ElementProfile = typeof(EventRefCanonicalHashProfile),
        CollectionOrderMode = CanonicalHashCollectionOrderMode.OrdinalByProperty,
        OrderByProperty = "Namespace,Id,Version")]
    [CanonicalHashField(nameof(CapabilityDescriptor.Consumes), CanonicalHashFieldClassification.DefinitionOnly, Order = 140,
        ElementProfile = typeof(EventRefCanonicalHashProfile),
        CollectionOrderMode = CanonicalHashCollectionOrderMode.OrdinalByProperty,
        OrderByProperty = "Namespace,Id,Version")]

    // ── Excluded fields ──

    [CanonicalHashField(nameof(CapabilityDescriptor.Namespace), CanonicalHashFieldClassification.Excluded,
        Reason = "Runtime constant — not part of hash")]
    [CanonicalHashField(nameof(CapabilityDescriptor.Kind), CanonicalHashFieldClassification.Excluded,
        Reason = "Runtime constant — not part of hash")]
    private static void Fields() { }
}
