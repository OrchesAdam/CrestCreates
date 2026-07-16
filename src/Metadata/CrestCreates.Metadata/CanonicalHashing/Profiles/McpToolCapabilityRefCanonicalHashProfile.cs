using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorCapability;

namespace CrestCreates.Metadata.CanonicalHashing.Profiles;

[CanonicalHashProfile(
    ArtifactKind = CanonicalHashArtifactKind.Descriptor,
    DescriptorKind = DescriptorKind.Unknown,
    TargetType = typeof(CapabilityProjectionReference),
    ContractShapeVersion = "mcp-tool-capability-ref-hash-v1",
    DefinitionShapeVersion = "mcp-tool-capability-ref-hash-v1")]
internal sealed class McpToolCapabilityRefCanonicalHashProfile
{
    [CanonicalHashField(nameof(CapabilityProjectionReference.Id), CanonicalHashFieldClassification.Contract, Order = 0)]
    [CanonicalHashField(nameof(CapabilityProjectionReference.Version), CanonicalHashFieldClassification.Contract, Order = 1)]
    [CanonicalHashField(nameof(CapabilityProjectionReference.SelectionMode), CanonicalHashFieldClassification.Contract, Order = 2)]
    [CanonicalHashField(nameof(CapabilityProjectionReference.ExpectedContractHash), CanonicalHashFieldClassification.Contract, Order = 3)]
    private static void Fields() { }
}
