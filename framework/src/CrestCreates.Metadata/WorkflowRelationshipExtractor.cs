using CrestCreates.Metadata.Abstractions;
using CrestCreates.Workflow.Abstractions;

namespace CrestCreates.Metadata;

public sealed class WorkflowRelationshipExtractor
    : DescriptorRelationshipExtractorBase<WorkflowDescriptor>
{
    public override DescriptorKind SupportedKind => DescriptorKind.Workflow;

    protected override IReadOnlyList<DescriptorRelationship> Extract(WorkflowDescriptor descriptor)
    {
        var relationships = new List<DescriptorRelationship>();

        foreach (var step in descriptor.Steps)
        {
            DescriptorRef? targetRef = step.Target switch
            {
                CapabilityTarget ct => new DescriptorRef(
                    "capability", ct.Capability.Id, ct.Capability.Version),
                HumanTaskTarget ht => new DescriptorRef(
                    "humantask", ht.HumanTask.Id, ht.HumanTask.Version),
                SubWorkflowTarget sw => new DescriptorRef(
                    "workflow", sw.SubWorkflow.Id, sw.SubWorkflow.Version),
                _ => null
            };

            if (targetRef is null)
                continue;

            relationships.Add(new DescriptorRelationship(
                From: new DescriptorRef("workflow", descriptor.Id, descriptor.Version),
                To: targetRef.Value,
                Kind: RelationshipKind.Uses,
                SourcePath: $"Steps.{step.Id}.Target",
                Strength: RelationshipStrength.Strong));
        }

        return relationships;
    }
}
