using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;

namespace CrestCreates.Metadata.CanonicalHashing.Profiles;

/// <summary>
/// Canonical hash profile for <see cref="SchemaFieldDescriptor"/>.
/// All fields participate in both ContractHash and DefinitionHash
/// of the parent <see cref="SchemaDescriptor"/>, so every field
/// is classified as <see cref="CanonicalHashFieldClassification.Contract"/>.
///
/// <b>Design Decision</b>: Optional field additions (IsRequired=false) are treated
/// as contract changes. This is a conservative stance — adding an optional field
/// changes the ContractHash, signaling that consumers should be aware of the new field.
/// The alternative (optional fields → DefinitionOnly only) was rejected because:
/// (a) conditional field classification would require per-instance runtime dispatch,
///     defeating compile-time determinism; (b) even optional fields change the
///     structural contract that consumers must be prepared to handle.
/// If finer-grained classification is needed in the future, a sub-classification
/// mechanism can be added without breaking existing hashes (via ContractShapeVersion bump).
/// </summary>
[CanonicalHashProfile(
    ArtifactKind = CanonicalHashArtifactKind.Descriptor,
    TargetType = typeof(SchemaFieldDescriptor),
    ContractShapeVersion = "schema-field-contract-hash-v1",
    DefinitionShapeVersion = "schema-field-definition-hash-v1")]
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
