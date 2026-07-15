using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Mcp;

namespace CrestCreates.Metadata.CanonicalHashing.Profiles;

[CanonicalHashProfile(
    ArtifactKind = CanonicalHashArtifactKind.Descriptor,
    DescriptorKind = DescriptorKind.Unknown,
    TargetType = typeof(McpCapabilityReference),
    ContractShapeVersion = "mcp-tool-capability-ref-hash-v1",
    DefinitionShapeVersion = "mcp-tool-capability-ref-hash-v1")]
internal sealed class McpToolCapabilityRefCanonicalHashProfile
{
    [CanonicalHashField(nameof(McpCapabilityReference.Id), CanonicalHashFieldClassification.Contract, Order = 0)]
    [CanonicalHashField(nameof(McpCapabilityReference.Version), CanonicalHashFieldClassification.Contract, Order = 1)]
    [CanonicalHashField(nameof(McpCapabilityReference.SelectionMode), CanonicalHashFieldClassification.Contract, Order = 2)]
    [CanonicalHashField(nameof(McpCapabilityReference.ExpectedContractHash), CanonicalHashFieldClassification.Contract, Order = 3)]
    private static void Fields() { }
}
