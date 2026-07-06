using CrestCreates.DynamicApi;
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Metadata.CanonicalHashing.Profiles;

[CanonicalHashProfile(
    ArtifactKind = CanonicalHashArtifactKind.Descriptor,
    DescriptorKind = DescriptorKind.Unknown,
    TargetType = typeof(CapabilityEndpointProjectionMetadata),
    ContractShapeVersion = "capability-endpoint-projection-hash-v1",
    DefinitionShapeVersion = "capability-endpoint-projection-hash-v1")]
internal sealed class CapabilityEndpointProjectionMetadataCanonicalHashProfile
{
    [CanonicalHashField(nameof(CapabilityEndpointProjectionMetadata.OperationId), CanonicalHashFieldClassification.Contract, Order = 0)]
    [CanonicalHashField(nameof(CapabilityEndpointProjectionMetadata.GroupName), CanonicalHashFieldClassification.DefinitionOnly, Order = 10)]
    [CanonicalHashField(nameof(CapabilityEndpointProjectionMetadata.Tags), CanonicalHashFieldClassification.DefinitionOnly, Order = 20,
        CollectionOrderMode = CanonicalHashCollectionOrderMode.OrdinalByValue)]
    [CanonicalHashField(nameof(CapabilityEndpointProjectionMetadata.Summary), CanonicalHashFieldClassification.DefinitionOnly, Order = 30)]
    [CanonicalHashField(nameof(CapabilityEndpointProjectionMetadata.Description), CanonicalHashFieldClassification.DefinitionOnly, Order = 40)]
    [CanonicalHashField(nameof(CapabilityEndpointProjectionMetadata.Deprecated), CanonicalHashFieldClassification.DefinitionOnly, Order = 50)]
    [CanonicalHashField(nameof(CapabilityEndpointProjectionMetadata.Visibility), CanonicalHashFieldClassification.DefinitionOnly, Order = 60)]
    private static void Fields() { }
}
