using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorRelationship;
using CrestCreates.Metadata.Mcp;

namespace CrestCreates.Mcp;

public sealed class McpToolRelationshipExtractor
    : DescriptorRelationshipExtractorBase<McpToolDescriptor>
{
    public override DescriptorKind SupportedKind => DescriptorKind.McpTool;

    protected override IReadOnlyList<DescriptorRelationship> Extract(McpToolDescriptor descriptor)
        =>
        [
            new DescriptorRelationship(
                From: new DescriptorRef(descriptor.Namespace, descriptor.Id, descriptor.Version),
                To: new DescriptorRef("capability", descriptor.Capability.Id, descriptor.Capability.Version),
                Kind: RelationshipKind.References,
                Role: "Capability",
                SourcePath: nameof(McpToolDescriptor.Capability),
                Strength: RelationshipStrength.Strong,
                IsRuntimeBinding: false)
        ];
}
