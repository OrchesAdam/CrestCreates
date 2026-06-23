using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;

namespace CrestCreates.Metadata.CanonicalHashing.Profiles;

/// <summary>
/// Canonical hash profile for <see cref="VersionedDescriptorRef{TDescriptor}"/>
/// specialized for <see cref="SchemaDescriptor"/>.
///
/// Only <c>Id</c> and <c>Version</c> participate in the hash (both ContractHash
/// and DefinitionHash). <c>SelectionMode</c> and <c>ExpectedContractHash</c>
/// are protocol-level concerns excluded from hashing.
/// </summary>
[CanonicalHashProfile(
    ArtifactKind = CanonicalHashArtifactKind.Descriptor,
    TargetType = typeof(VersionedDescriptorRef<SchemaDescriptor>),
    ContractShapeVersion = "versioned-schema-ref-hash-v1",
    DefinitionShapeVersion = "versioned-schema-ref-hash-v1")]
internal sealed class VersionedSchemaRefCanonicalHashProfile
{
    [CanonicalHashField(nameof(VersionedDescriptorRef<SchemaDescriptor>.Id), CanonicalHashFieldClassification.Contract, Order = 0)]
    [CanonicalHashField(nameof(VersionedDescriptorRef<SchemaDescriptor>.Version), CanonicalHashFieldClassification.Contract, Order = 1)]

    [CanonicalHashField(nameof(VersionedDescriptorRef<SchemaDescriptor>.SelectionMode), CanonicalHashFieldClassification.Excluded,
        Reason = "Protocol-level resolution concern — not part of structural hash")]
    [CanonicalHashField(nameof(VersionedDescriptorRef<SchemaDescriptor>.ExpectedContractHash), CanonicalHashFieldClassification.Excluded,
        Reason = "Resolution-time input — not part of structural hash")]

    private static void Fields() { }
}
