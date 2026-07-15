using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Mcp;

namespace CrestCreates.Metadata.CanonicalHashing.Profiles;

[CanonicalHashProfile(
    ArtifactKind = CanonicalHashArtifactKind.Descriptor,
    DescriptorKind = DescriptorKind.Unknown,
    TargetType = typeof(McpToolAnnotationOverrides),
    ContractShapeVersion = "mcp-tool-annotation-overrides-hash-v1",
    DefinitionShapeVersion = "mcp-tool-annotation-overrides-hash-v1")]
internal sealed class McpToolAnnotationOverridesCanonicalHashProfile
{
    [CanonicalHashField(nameof(McpToolAnnotationOverrides.DestructiveHint), CanonicalHashFieldClassification.Contract, Order = 0)]
    [CanonicalHashField(nameof(McpToolAnnotationOverrides.IdempotentHint), CanonicalHashFieldClassification.Contract, Order = 1)]
    [CanonicalHashField(nameof(McpToolAnnotationOverrides.OpenWorldHint), CanonicalHashFieldClassification.Contract, Order = 2)]
    private static void Fields() { }
}
