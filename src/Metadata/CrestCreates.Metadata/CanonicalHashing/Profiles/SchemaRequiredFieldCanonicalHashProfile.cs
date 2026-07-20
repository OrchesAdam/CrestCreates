using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;

namespace CrestCreates.Metadata.CanonicalHashing.Profiles;

/// <summary>
/// Canonical hash profile for <see cref="SchemaFieldDescriptor"/> that captures only the
/// required-binding surface (fields that constitute the read/write contract).
/// Used as the ElementProfile for <see cref="SchemaDescriptor.Fields"/> in ContractHash (v2).
///
/// Contract fields (in both ContractHash and DefinitionHash):
///   Name, FieldType, IsRequired, IsNullable, IsCollection, CollectionElementType,
///   MaxLength, MinLength, MaxValue, MinValue, Pattern
/// </summary>
[CanonicalHashProfile(
    ArtifactKind = CanonicalHashArtifactKind.Descriptor,
    DescriptorKind = DescriptorKind.Unknown,
    TargetType = typeof(SchemaFieldDescriptor),
    ContractShapeVersion = "schema-required-field-contract-hash-v1",
    DefinitionShapeVersion = "schema-required-field-definition-hash-v1")]
internal sealed class SchemaRequiredFieldCanonicalHashProfile
{
    [CanonicalHashField(nameof(SchemaFieldDescriptor.Name), CanonicalHashFieldClassification.Contract, Order = 0)]
    [CanonicalHashField(nameof(SchemaFieldDescriptor.FieldType), CanonicalHashFieldClassification.Contract, Order = 1)]
    [CanonicalHashField(nameof(SchemaFieldDescriptor.IsRequired), CanonicalHashFieldClassification.Contract, Order = 2)]
    [CanonicalHashField(nameof(SchemaFieldDescriptor.IsNullable), CanonicalHashFieldClassification.Contract, Order = 3)]
    [CanonicalHashField(nameof(SchemaFieldDescriptor.IsCollection), CanonicalHashFieldClassification.Contract, Order = 4)]
    [CanonicalHashField(nameof(SchemaFieldDescriptor.CollectionElementType), CanonicalHashFieldClassification.Contract, Order = 5)]
    [CanonicalHashField(nameof(SchemaFieldDescriptor.ObjectSchema), CanonicalHashFieldClassification.Excluded,
        Reason = "Nested object references are represented by the Schema v3 projection.")]
    [CanonicalHashField(nameof(SchemaFieldDescriptor.MaxLength), CanonicalHashFieldClassification.Contract, Order = 6)]
    [CanonicalHashField(nameof(SchemaFieldDescriptor.MinLength), CanonicalHashFieldClassification.Contract, Order = 7)]
    [CanonicalHashField(nameof(SchemaFieldDescriptor.MaxValue), CanonicalHashFieldClassification.Contract, Order = 8)]
    [CanonicalHashField(nameof(SchemaFieldDescriptor.MinValue), CanonicalHashFieldClassification.Contract, Order = 9)]
    [CanonicalHashField(nameof(SchemaFieldDescriptor.Pattern), CanonicalHashFieldClassification.Contract, Order = 10)]

    private static void Fields() { }
}
