using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorRelationship;
using CrestCreates.Schema.Abstractions;
using Relationship = CrestCreates.Metadata.Abstractions.DescriptorRelationship.DescriptorRelationship;

namespace CrestCreates.Metadata.DescriptorRelationship;

public sealed class SchemaRelationshipExtractor
    : DescriptorRelationshipExtractorBase<SchemaDescriptor>
{
    public override DescriptorKind SupportedKind => DescriptorKind.Schema;

    protected override IReadOnlyList<Relationship> Extract(SchemaDescriptor descriptor)
    {
        var relationships = new List<Relationship>();

        foreach (var reference in descriptor.References)
        {
            relationships.Add(new Relationship(
                From: new DescriptorRef("schema", descriptor.Id, descriptor.Version),
                To: new DescriptorRef("schema", reference.Id, reference.Version),
                Kind: RelationshipKind.References,
                SourcePath: "References",
                Strength: RelationshipStrength.Weak));
        }

        return relationships;
    }
}
