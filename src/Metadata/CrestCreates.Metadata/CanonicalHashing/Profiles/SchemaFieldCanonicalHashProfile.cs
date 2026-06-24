using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;

namespace CrestCreates.Metadata.CanonicalHashing.Profiles;

/// <summary>
/// Canonical hash profile for <see cref="SchemaFieldDescriptor"/>.
/// Full field descriptor — all fields participate in DefinitionHash.
///
/// v2: This profile is now used only for the full-field representation
/// in Schema DefinitionHash. Schema ContractHash uses
/// <see cref="SchemaRequiredFieldCanonicalHashProfile"/> which captures
/// only the required-binding surface, filtered by
/// <see cref="RequiredSchemaFieldCanonicalHashFilter"/>.
/// Optional fields are excluded from ContractHash but still included
/// in DefinitionHash via this profile.
/// </summary>
[CanonicalHashProfile(
    ArtifactKind = CanonicalHashArtifactKind.Descriptor,
    TargetType = typeof(SchemaFieldDescriptor),
    ContractShapeVersion = "schema-field-contract-hash-v2",
    DefinitionShapeVersion = "schema-field-definition-hash-v2")]
internal sealed class SchemaFieldCanonicalHashProfile
{
    [CanonicalHashField(nameof(SchemaFieldDescriptor.Name), CanonicalHashFieldClassification.Contract, Order = 0)]
    [CanonicalHashField(nameof(SchemaFieldDescriptor.FieldType), CanonicalHashFieldClassification.Contract, Order = 1)]
    [CanonicalHashField(nameof(SchemaFieldDescriptor.IsRequired), CanonicalHashFieldClassification.Contract, Order = 2)]
    [CanonicalHashField(nameof(SchemaFieldDescriptor.IsNullable), CanonicalHashFieldClassification.Contract, Order = 3)]
    [CanonicalHashField(nameof(SchemaFieldDescriptor.MaxLength), CanonicalHashFieldClassification.Contract, Order = 4)]
    [CanonicalHashField(nameof(SchemaFieldDescriptor.MinLength), CanonicalHashFieldClassification.Contract, Order = 5)]
    [CanonicalHashField(nameof(SchemaFieldDescriptor.MaxValue), CanonicalHashFieldClassification.Contract, Order = 6)]
    [CanonicalHashField(nameof(SchemaFieldDescriptor.MinValue), CanonicalHashFieldClassification.Contract, Order = 7)]
    [CanonicalHashField(nameof(SchemaFieldDescriptor.Pattern), CanonicalHashFieldClassification.Contract, Order = 8)]
    [CanonicalHashField(nameof(SchemaFieldDescriptor.IsCollection), CanonicalHashFieldClassification.Contract, Order = 9)]
    [CanonicalHashField(nameof(SchemaFieldDescriptor.CollectionElementType), CanonicalHashFieldClassification.Contract, Order = 10)]

    private static void Fields() { }
}
