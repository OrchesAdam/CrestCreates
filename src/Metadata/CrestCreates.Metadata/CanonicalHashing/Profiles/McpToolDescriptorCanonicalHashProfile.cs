using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Mcp;

namespace CrestCreates.Metadata.CanonicalHashing.Profiles;

[CanonicalHashProfile(
    ArtifactKind = CanonicalHashArtifactKind.Descriptor,
    DescriptorKind = DescriptorKind.McpTool,
    TargetType = typeof(McpToolDescriptor),
    ContractShapeVersion = "mcp-tool-contract-hash-v1",
    DefinitionShapeVersion = "mcp-tool-definition-hash-v1")]
internal sealed class McpToolDescriptorCanonicalHashProfile
{
    [CanonicalHashField(nameof(McpToolDescriptor.Id), CanonicalHashFieldClassification.Contract, Order = 0)]
    [CanonicalHashField(nameof(McpToolDescriptor.Name), CanonicalHashFieldClassification.Contract, Order = 1)]
    [CanonicalHashField(nameof(McpToolDescriptor.Version), CanonicalHashFieldClassification.Contract, Order = 2)]
    [CanonicalHashField(nameof(McpToolDescriptor.State), CanonicalHashFieldClassification.Contract, Order = 3)]
    [CanonicalHashField(nameof(McpToolDescriptor.SupersededById), CanonicalHashFieldClassification.Contract, Order = 4)]
    [CanonicalHashField(nameof(McpToolDescriptor.ToolName), CanonicalHashFieldClassification.Contract, Order = 10)]
    [CanonicalHashField(nameof(McpToolDescriptor.Capability), CanonicalHashFieldClassification.Contract, Order = 11,
        ValueProfile = typeof(McpToolCapabilityRefCanonicalHashProfile))]
    [CanonicalHashField(nameof(McpToolDescriptor.Description), CanonicalHashFieldClassification.Contract, Order = 12)]
    [CanonicalHashField(nameof(McpToolDescriptor.AnnotationOverrides), CanonicalHashFieldClassification.Contract, Order = 13,
        ValueProfile = typeof(McpToolAnnotationOverridesCanonicalHashProfile))]
    [CanonicalHashField(nameof(McpToolDescriptor.Title), CanonicalHashFieldClassification.DefinitionOnly, Order = 20)]
    [CanonicalHashField(nameof(McpToolDescriptor.Namespace), CanonicalHashFieldClassification.Excluded,
        Reason = "Runtime constant — not part of hash")]
    [CanonicalHashField(nameof(McpToolDescriptor.Kind), CanonicalHashFieldClassification.Excluded,
        Reason = "Runtime constant — not part of hash")]
    private static void Fields() { }
}
