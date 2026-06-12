using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;
using CrestCreates.Workflow.Abstractions;

namespace CrestCreates.Workflow;

public sealed class WorkflowRelationshipExtractor
    : DescriptorRelationshipExtractorBase<WorkflowDescriptor>
{
    public override DescriptorKind SupportedKind => DescriptorKind.Workflow;

    protected override IReadOnlyList<DescriptorRelationship> Extract(WorkflowDescriptor descriptor)
    {
        var relationships = new List<DescriptorRelationship>();

        // VariableSchema → SchemaDescriptor (Uses, Strong) — nullable
        if (descriptor.VariableSchema.HasValue)
        {
            relationships.Add(new DescriptorRelationship(
                From: new DescriptorRef("workflow", descriptor.Id, descriptor.Version),
                To: new DescriptorRef("schema", descriptor.VariableSchema.Value.Id, descriptor.VariableSchema.Value.Version),
                Kind: RelationshipKind.Uses,
                Role: "VariableSchema",
                SourcePath: "VariableSchema",
                Strength: RelationshipStrength.Strong));
        }

        // Step targets
        foreach (var step in descriptor.Steps)
        {
            switch (step.Target)
            {
                case CapabilityTarget capabilityTarget:
                    relationships.Add(new DescriptorRelationship(
                        From: new DescriptorRef("workflow", descriptor.Id, descriptor.Version),
                        To: new DescriptorRef("capability", capabilityTarget.Capability.Id, capabilityTarget.Capability.Version),
                        Kind: RelationshipKind.Triggers,
                        Role: "CapabilityStep",
                        SourcePath: "Steps",
                        Strength: RelationshipStrength.Strong,
                        IsRuntimeBinding: true));
                    break;

                case HumanTaskTarget humanTaskTarget:
                    relationships.Add(new DescriptorRelationship(
                        From: new DescriptorRef("workflow", descriptor.Id, descriptor.Version),
                        To: new DescriptorRef("humantask", humanTaskTarget.HumanTask.Id, humanTaskTarget.HumanTask.Version),
                        Kind: RelationshipKind.Triggers,
                        Role: "HumanTaskStep",
                        SourcePath: "Steps",
                        Strength: RelationshipStrength.Strong,
                        IsRuntimeBinding: true));
                    break;

                case SubWorkflowTarget subWorkflowTarget:
                    relationships.Add(new DescriptorRelationship(
                        From: new DescriptorRef("workflow", descriptor.Id, descriptor.Version),
                        To: new DescriptorRef("workflow", subWorkflowTarget.SubWorkflow.Id, subWorkflowTarget.SubWorkflow.Version),
                        Kind: RelationshipKind.References,
                        Role: "SubWorkflowStep",
                        SourcePath: "Steps",
                        Strength: RelationshipStrength.Weak,
                        IsRuntimeBinding: false));
                    break;
            }
        }

        return relationships;
    }
}
