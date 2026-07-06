using CrestCreates.DynamicApi;
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Metadata.CanonicalHashing.Profiles;

[CanonicalHashProfile(
    ArtifactKind = CanonicalHashArtifactKind.Descriptor,
    DescriptorKind = DescriptorKind.Unknown,
    TargetType = typeof(CapabilityEndpointInputBinding),
    ContractShapeVersion = "capability-endpoint-input-binding-hash-v1",
    DefinitionShapeVersion = "capability-endpoint-input-binding-hash-v1")]
internal sealed class CapabilityEndpointInputBindingCanonicalHashProfile
{
    [CanonicalHashField(nameof(CapabilityEndpointInputBinding.Name), CanonicalHashFieldClassification.Contract, Order = 0)]
    [CanonicalHashField(nameof(CapabilityEndpointInputBinding.Source), CanonicalHashFieldClassification.Contract, Order = 1)]
    [CanonicalHashField(nameof(CapabilityEndpointInputBinding.CapabilityInputPath), CanonicalHashFieldClassification.Contract, Order = 2)]
    [CanonicalHashField(nameof(CapabilityEndpointInputBinding.Required), CanonicalHashFieldClassification.Contract, Order = 3)]
    private static void Fields() { }
}
