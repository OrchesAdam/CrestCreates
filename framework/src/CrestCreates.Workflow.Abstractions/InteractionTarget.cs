using CrestCreates.Capability.Abstractions;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Workflow.Abstractions;

public abstract record InteractionTarget
{
    protected InteractionTarget() { }
}

public sealed record CapabilityTarget : InteractionTarget
{
    public VersionedDescriptorRef<CapabilityDescriptor> Capability { get; init; }
}

public sealed record HumanTaskTarget : InteractionTarget
{
    public VersionedDescriptorRef<HumanTaskDescriptor> HumanTask { get; init; }
}

public sealed record SubWorkflowTarget : InteractionTarget
{
    public VersionedDescriptorRef<WorkflowDescriptor> SubWorkflow { get; init; }
}
