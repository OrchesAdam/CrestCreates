using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorRelationship;

namespace CrestCreates.DynamicApi;

public sealed class CapabilityEndpointRelationshipExtractor
    : DescriptorRelationshipExtractorBase<CapabilityEndpointDescriptor>
{
    public override DescriptorKind SupportedKind => DescriptorKind.DynamicApiEndpoint;

    protected override IReadOnlyList<DescriptorRelationship> Extract(
        CapabilityEndpointDescriptor descriptor)
    {
        return
        [
            new DescriptorRelationship(
                From: new DescriptorRef(
                    descriptor.Namespace,
                    descriptor.Id,
                    descriptor.Version),
                To: new DescriptorRef(
                    "capability",
                    descriptor.Capability.Id,
                    descriptor.Capability.Version),
                Kind: RelationshipKind.References,
                Role: "Capability",
                SourcePath: nameof(CapabilityEndpointDescriptor.Capability),
                Strength: RelationshipStrength.Strong,
                IsRuntimeBinding: false)
        ];
    }
}
