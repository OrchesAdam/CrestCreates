using CrestCreates.Metadata.AgentTool;
using CrestCreates.Metadata.Abstractions.DescriptorRelationship;
using CrestCreates.Metadata.Abstractions.DescriptorTopology;
using Relationship = CrestCreates.Metadata.Abstractions.DescriptorRelationship.DescriptorRelationship;

namespace CrestCreates.Metadata.DescriptorRelationship;

public sealed class AgentToolRelationshipExtractor
    : DescriptorRelationshipExtractorBase<AgentCapabilityToolDescriptor>
{
    public override DescriptorKind SupportedKind => DescriptorKind.AgentTool;

    protected override IReadOnlyList<Relationship> Extract(
        AgentCapabilityToolDescriptor descriptor)
    {
        return
        [
            new Relationship(
                From: new DescriptorRef(
                    descriptor.Namespace,
                    descriptor.Id,
                    descriptor.Version),
                To: new DescriptorRef(
                    "capability",
                    descriptor.Capability.Id,
                    descriptor.Capability.Version),
                Kind: RelationshipKind.References,
                Role: RelationshipRoles.Capability,
                SourcePath: nameof(AgentCapabilityToolDescriptor.Capability),
                Strength: RelationshipStrength.Strong)
        ];
    }
}
