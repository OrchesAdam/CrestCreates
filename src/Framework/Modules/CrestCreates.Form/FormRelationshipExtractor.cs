using CrestCreates.Form.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorRelationship;
using CrestCreates.Schema.Abstractions;

namespace CrestCreates.Form;

public sealed class FormRelationshipExtractor
    : DescriptorRelationshipExtractorBase<FormDescriptor>
{
    public override DescriptorKind SupportedKind => DescriptorKind.Form;

    protected override IReadOnlyList<DescriptorRelationship> Extract(FormDescriptor descriptor)
    {
        var relationships = new List<DescriptorRelationship>
        {
            new(
                From: new DescriptorRef("form", descriptor.Id, descriptor.Version),
                To: new DescriptorRef("schema", descriptor.Schema.Id, descriptor.Schema.Version),
                Kind: RelationshipKind.Uses,
                Role: "Schema",
                SourcePath: "Schema",
                Strength: RelationshipStrength.Strong)
        };

        return relationships;
    }
}
