using CrestCreates.Form.Abstractions;
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Metadata.CanonicalHashing.Profiles;

/// <summary>
/// Canonical hash profile for <see cref="FormDescriptor"/>.
///
/// Contract fields (in both ContractHash and DefinitionHash):
///   Id, Name, Version, State, SupersededById, Schema, Fields
///
/// DefinitionOnly fields (only in DefinitionHash):
///   LayoutColumns
/// </summary>
[CanonicalHashProfile(
    ArtifactKind = CanonicalHashArtifactKind.Descriptor,
    DescriptorKind = DescriptorKind.Form,
    TargetType = typeof(FormDescriptor),
    ContractShapeVersion = "form-contract-hash-v1",
    DefinitionShapeVersion = "form-definition-hash-v1")]
internal sealed class FormDescriptorCanonicalHashProfile
{
    // ── Contract fields (common to both ContractHash and DefinitionHash) ──

    [CanonicalHashField(nameof(FormDescriptor.Id), CanonicalHashFieldClassification.Contract, Order = 0)]
    [CanonicalHashField(nameof(FormDescriptor.Name), CanonicalHashFieldClassification.Contract, Order = 1)]
    [CanonicalHashField(nameof(FormDescriptor.Version), CanonicalHashFieldClassification.Contract, Order = 2)]
    [CanonicalHashField(nameof(FormDescriptor.State), CanonicalHashFieldClassification.Contract, Order = 3)]
    [CanonicalHashField(nameof(FormDescriptor.SupersededById), CanonicalHashFieldClassification.Contract, Order = 4)]
    [CanonicalHashField(nameof(FormDescriptor.Schema), CanonicalHashFieldClassification.Contract, Order = 10,
        ValueProfile = typeof(VersionedSchemaRefCanonicalHashProfile))]
    [CanonicalHashField(nameof(FormDescriptor.Fields), CanonicalHashFieldClassification.Contract, Order = 20,
        ElementProfile = typeof(FormFieldCanonicalHashProfile),
        CollectionOrderMode = CanonicalHashCollectionOrderMode.OrdinalByProperty,
        OrderByProperty = "SchemaFieldName")]

    // ── DefinitionOnly fields (only in DefinitionHash) ──

    [CanonicalHashField(nameof(FormDescriptor.LayoutColumns), CanonicalHashFieldClassification.DefinitionOnly, Order = 110)]

    // ── Excluded fields ──

    [CanonicalHashField(nameof(FormDescriptor.Namespace), CanonicalHashFieldClassification.Excluded,
        Reason = "Runtime constant — not part of hash")]
    [CanonicalHashField(nameof(FormDescriptor.Kind), CanonicalHashFieldClassification.Excluded,
        Reason = "Runtime constant — not part of hash")]
    private static void Fields() { }
}
