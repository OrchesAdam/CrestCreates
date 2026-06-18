using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;

namespace CrestCreates.HumanTask;

public sealed class HumanTaskRelationshipExtractor
    : DescriptorRelationshipExtractorBase<HumanTaskDescriptor>
{
    public override DescriptorKind SupportedKind => DescriptorKind.HumanTask;

    protected override IReadOnlyList<DescriptorRelationship> Extract(HumanTaskDescriptor descriptor)
    {
        var relationships = new List<DescriptorRelationship>();

        // Interaction → FormDescriptor (Uses, Strong)
        relationships.Add(new DescriptorRelationship(
            From: new DescriptorRef("humantask", descriptor.Id, descriptor.Version),
            To: new DescriptorRef("form", descriptor.Interaction.Id, descriptor.Interaction.Version),
            Kind: RelationshipKind.Uses,
            Role: "Interaction",
            SourcePath: "Interaction",
            Strength: RelationshipStrength.Strong));

        // InputSchema → SchemaDescriptor (Consumes, Strong) — nullable
        if (descriptor.InputSchema.HasValue)
        {
            relationships.Add(new DescriptorRelationship(
                From: new DescriptorRef("humantask", descriptor.Id, descriptor.Version),
                To: new DescriptorRef("schema", descriptor.InputSchema.Value.Id, descriptor.InputSchema.Value.Version),
                Kind: RelationshipKind.Consumes,
                Role: "InputSchema",
                SourcePath: "InputSchema",
                Strength: RelationshipStrength.Strong));
        }

        // OutputSchema → SchemaDescriptor (Produces, Strong) — nullable
        if (descriptor.OutputSchema.HasValue)
        {
            relationships.Add(new DescriptorRelationship(
                From: new DescriptorRef("humantask", descriptor.Id, descriptor.Version),
                To: new DescriptorRef("schema", descriptor.OutputSchema.Value.Id, descriptor.OutputSchema.Value.Version),
                Kind: RelationshipKind.Produces,
                Role: "OutputSchema",
                SourcePath: "OutputSchema",
                Strength: RelationshipStrength.Strong));
        }

        // Outcomes[].Capability → CapabilityDescriptor (Triggers, Strong)
        foreach (var outcome in descriptor.Outcomes)
        {
            if (!outcome.Capability.HasValue) continue;

            relationships.Add(new DescriptorRelationship(
                From: new DescriptorRef("humantask", descriptor.Id, descriptor.Version),
                To: new DescriptorRef("capability", outcome.Capability.Value.Id, outcome.Capability.Value.Version),
                Kind: RelationshipKind.Triggers,
                Role: "Outcome",
                SourcePath: "Outcomes",
                Strength: RelationshipStrength.Strong,
                IsRuntimeBinding: true));
        }

        return relationships;
    }
}
