using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;

namespace CrestCreates.Capability;

public sealed class CapabilityRelationshipExtractor
    : DescriptorRelationshipExtractorBase<CapabilityDescriptor>
{
    public override DescriptorKind SupportedKind => DescriptorKind.Capability;

    protected override IReadOnlyList<DescriptorRelationship> Extract(CapabilityDescriptor descriptor)
    {
        var relationships = new List<DescriptorRelationship>();

        // InputSchema → SchemaDescriptor (Consumes, Strong)
        if (descriptor.InputSchema.HasValue)
        {
            relationships.Add(new DescriptorRelationship(
                From: new DescriptorRef("capability", descriptor.Id, descriptor.Version),
                To: new DescriptorRef("schema", descriptor.InputSchema.Value.Id, descriptor.InputSchema.Value.Version),
                Kind: RelationshipKind.Consumes,
                Role: "InputSchema",
                SourcePath: "InputSchema",
                Strength: RelationshipStrength.Strong));
        }

        // OutputSchema → SchemaDescriptor (Produces, Strong)
        if (descriptor.OutputSchema.HasValue)
        {
            relationships.Add(new DescriptorRelationship(
                From: new DescriptorRef("capability", descriptor.Id, descriptor.Version),
                To: new DescriptorRef("schema", descriptor.OutputSchema.Value.Id, descriptor.OutputSchema.Value.Version),
                Kind: RelationshipKind.Produces,
                Role: "OutputSchema",
                SourcePath: "OutputSchema",
                Strength: RelationshipStrength.Strong));
        }

        // Produces[] → Event descriptors (Produces, Weak)
        foreach (var @event in descriptor.Produces)
        {
            relationships.Add(new DescriptorRelationship(
                From: new DescriptorRef("capability", descriptor.Id, descriptor.Version),
                To: new DescriptorRef(@event.Namespace, @event.Id, @event.Version),
                Kind: RelationshipKind.Produces,
                SourcePath: "Produces",
                Strength: RelationshipStrength.Weak));
        }

        // Consumes[] → Event descriptors (Consumes, Weak)
        foreach (var @event in descriptor.Consumes)
        {
            relationships.Add(new DescriptorRelationship(
                From: new DescriptorRef("capability", descriptor.Id, descriptor.Version),
                To: new DescriptorRef(@event.Namespace, @event.Id, @event.Version),
                Kind: RelationshipKind.Consumes,
                SourcePath: "Consumes",
                Strength: RelationshipStrength.Weak));
        }

        // SupersededById → CapabilityDescriptor (DependsOn, Weak)
        if (descriptor.SupersededById is not null)
        {
            relationships.Add(new DescriptorRelationship(
                From: new DescriptorRef("capability", descriptor.Id, descriptor.Version),
                To: new DescriptorRef("capability", descriptor.SupersededById),
                Kind: RelationshipKind.DependsOn,
                Role: "SupersededBy",
                SourcePath: "SupersededById",
                Strength: RelationshipStrength.Weak));
        }

        return relationships;
    }
}
