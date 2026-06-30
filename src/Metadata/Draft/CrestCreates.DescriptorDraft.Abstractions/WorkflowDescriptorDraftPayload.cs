using CrestCreates.Metadata.Abstractions;
using CrestCreates.Workflow.Abstractions;

namespace CrestCreates.DescriptorDraft.Abstractions;

public sealed record WorkflowDescriptorDraftPayload(
    WorkflowDescriptor Descriptor
) : DescriptorDraftPayload
{
    public override DescriptorKind DescriptorKind => DescriptorKind.Workflow;
    public override IDescriptor GetDescriptor() => Descriptor;
    public override DescriptorDraftPayload Snapshot() => this with
    {
        Descriptor = new WorkflowDescriptor
        {
            Id = Descriptor.Id,
            Name = Descriptor.Name,
            State = Descriptor.State,
            SupersededById = Descriptor.SupersededById,
            Version = Descriptor.Version,
            VariableSchema = Descriptor.VariableSchema,
            Steps = Descriptor.Steps.Select(CloneStep).ToArray(),
            DefaultVariableScope = Descriptor.DefaultVariableScope
        }
    };

    private static WorkflowStep CloneStep(WorkflowStep step) => new()
    {
        Id = step.Id,
        Name = step.Name,
        Target = CloneTarget(step.Target),
        Condition = step.Condition,
        Transitions = step.Transitions.ToArray(),
        InputMapping = step.InputMapping,
        OutputMapping = step.OutputMapping,
        OnError = step.OnError
    };

    private static InteractionTarget CloneTarget(InteractionTarget target) => target switch
    {
        CapabilityTarget capabilityTarget => new CapabilityTarget
        {
            Capability = capabilityTarget.Capability
        },
        HumanTaskTarget humanTaskTarget => new HumanTaskTarget
        {
            HumanTask = humanTaskTarget.HumanTask
        },
        SubWorkflowTarget subWorkflowTarget => new SubWorkflowTarget
        {
            SubWorkflow = subWorkflowTarget.SubWorkflow
        },
        _ => throw new ArgumentOutOfRangeException(nameof(target), target.GetType(), "Unsupported workflow target type.")
    };
}
