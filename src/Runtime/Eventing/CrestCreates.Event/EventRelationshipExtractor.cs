using CrestCreates.Event.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorRelationship;
using CrestCreates.Schema.Abstractions;

namespace CrestCreates.Event;

public sealed class EventRelationshipExtractor
    : DescriptorRelationshipExtractorBase<GeneratedEventDescriptor>
{
    public override DescriptorKind SupportedKind => DescriptorKind.Event;

    protected override IReadOnlyList<DescriptorRelationship> Extract(GeneratedEventDescriptor descriptor)
    {
        var relationships = new List<DescriptorRelationship>();

        // PayloadSchemaRef → SchemaDescriptor (Uses, Strong)
        // PayloadSchemaRef is a VersionedDescriptorRef<SchemaDescriptor> record struct — never null.
        // Emit relationship even if Id is empty (structural validation is validator's job).
        relationships.Add(new DescriptorRelationship(
            From: new DescriptorRef("event", descriptor.Id, descriptor.Version),
            To: new DescriptorRef("schema", descriptor.PayloadSchemaRef.Id, descriptor.PayloadSchemaRef.Version),
            Kind: RelationshipKind.Uses,
            Role: "PayloadSchema",
            SourcePath: "PayloadSchemaRef",
            Strength: RelationshipStrength.Strong));

        return relationships;
    }
}
