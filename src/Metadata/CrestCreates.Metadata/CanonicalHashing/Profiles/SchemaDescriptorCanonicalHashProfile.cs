using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;

namespace CrestCreates.Metadata.CanonicalHashing.Profiles;

/// <summary>
/// Canonical hash profile for <see cref="SchemaDescriptor"/>.
/// Field classifications are derived from <c>DescriptorStableHashBuilder</c>
/// as the authoritative source of truth.
///
/// Contract fields (in both ContractHash and DefinitionHash):
///   Id, Name, Version, ChangeKind, State, SupersededById,
///   Fields (ordered by Name), References (ordered by Id+Version)
///
/// DefinitionOnly fields (only in DefinitionHash):
///   ValidationRules (ordered by Name)
///
/// Excluded fields:
///   Namespace, Kind (computed constants),
///   ContractHash, DefinitionHash (hash outputs)
/// </summary>
[CanonicalHashProfile(
    ArtifactKind = CanonicalHashArtifactKind.Descriptor,
    DescriptorKind = DescriptorKind.Schema,
    TargetType = typeof(SchemaDescriptor),
    ContractShapeVersion = "schema-contract-hash-v1",
    DefinitionShapeVersion = "schema-definition-hash-v1")]
internal sealed class SchemaDescriptorCanonicalHashProfile
{
    // ── Contract fields (common to both ContractHash and DefinitionHash) ──

    [CanonicalHashField(nameof(SchemaDescriptor.Id), CanonicalHashFieldClassification.Contract, Order = 0)]
    [CanonicalHashField(nameof(SchemaDescriptor.Name), CanonicalHashFieldClassification.Contract, Order = 1)]
    [CanonicalHashField(nameof(SchemaDescriptor.Version), CanonicalHashFieldClassification.Contract, Order = 2)]
    [CanonicalHashField(nameof(SchemaDescriptor.ChangeKind), CanonicalHashFieldClassification.Contract, Order = 3)]
    [CanonicalHashField(nameof(SchemaDescriptor.State), CanonicalHashFieldClassification.Contract, Order = 4)]
    [CanonicalHashField(nameof(SchemaDescriptor.SupersededById), CanonicalHashFieldClassification.Contract, Order = 5)]
    [CanonicalHashField(nameof(SchemaDescriptor.Fields), CanonicalHashFieldClassification.Contract, Order = 10,
        ElementProfile = typeof(SchemaFieldCanonicalHashProfile),
        CollectionOrderMode = CanonicalHashCollectionOrderMode.OrdinalByProperty,
        OrderByProperty = nameof(SchemaFieldDescriptor.Name))]
    [CanonicalHashField(nameof(SchemaDescriptor.References), CanonicalHashFieldClassification.Contract, Order = 20,
        ElementProfile = typeof(VersionedSchemaRefCanonicalHashProfile),
        CollectionOrderMode = CanonicalHashCollectionOrderMode.OrdinalByProperty,
        OrderByProperty = "Id,Version")]

    // ── DefinitionOnly fields (only in DefinitionHash) ──

    [CanonicalHashField(nameof(SchemaDescriptor.ValidationRules), CanonicalHashFieldClassification.DefinitionOnly, Order = 110,
        ElementProfile = typeof(SchemaValidationRuleCanonicalHashProfile),
        CollectionOrderMode = CanonicalHashCollectionOrderMode.OrdinalByProperty,
        OrderByProperty = "Name,Expression,ErrorMessage")]

    // ── Excluded fields ──

    [CanonicalHashField(nameof(SchemaDescriptor.Namespace), CanonicalHashFieldClassification.Excluded,
        Reason = "Runtime constant ('schema') — not part of hash")]
    [CanonicalHashField(nameof(SchemaDescriptor.Kind), CanonicalHashFieldClassification.Excluded,
        Reason = "Runtime constant (DescriptorKind.Schema) — not part of hash")]
    private static void Fields() { }
}
