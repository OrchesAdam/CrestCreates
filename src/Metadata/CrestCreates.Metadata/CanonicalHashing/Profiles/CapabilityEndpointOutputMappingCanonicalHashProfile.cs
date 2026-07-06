using CrestCreates.DynamicApi;
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Metadata.CanonicalHashing.Profiles;

[CanonicalHashProfile(
    ArtifactKind = CanonicalHashArtifactKind.Descriptor,
    DescriptorKind = DescriptorKind.Unknown,
    TargetType = typeof(CapabilityEndpointOutputMapping),
    ContractShapeVersion = "capability-endpoint-output-mapping-hash-v1",
    DefinitionShapeVersion = "capability-endpoint-output-mapping-hash-v1")]
internal sealed class CapabilityEndpointOutputMappingCanonicalHashProfile
{
    [CanonicalHashField(nameof(CapabilityEndpointOutputMapping.SuccessStatusCode), CanonicalHashFieldClassification.Contract, Order = 0)]
    [CanonicalHashField(nameof(CapabilityEndpointOutputMapping.ContentType), CanonicalHashFieldClassification.Contract, Order = 1)]
    private static void Fields() { }
}
