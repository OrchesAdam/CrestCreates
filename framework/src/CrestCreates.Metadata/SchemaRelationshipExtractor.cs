using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;

namespace CrestCreates.Metadata;

public sealed class SchemaRelationshipExtractor
    : DescriptorRelationshipExtractorBase<SchemaDescriptor>
{
    public override DescriptorKind SupportedKind => DescriptorKind.Schema;

    protected override IReadOnlyList<DescriptorRelationship> Extract(SchemaDescriptor descriptor)
    {
        var relationships = new List<DescriptorRelationship>();

        foreach (var reference in descriptor.References)
        {
            relationships.Add(new DescriptorRelationship(
                From: new DescriptorRef("schema", descriptor.Id, descriptor.Version),
                To: new DescriptorRef("schema", reference.Id, reference.Version),
                Kind: RelationshipKind.References,
                SourcePath: "References",
                Strength: RelationshipStrength.Weak));
        }

        return relationships;
    }
}
