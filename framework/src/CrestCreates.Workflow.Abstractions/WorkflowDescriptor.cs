using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;

namespace CrestCreates.Workflow.Abstractions;

public sealed class WorkflowDescriptor : IVersionedDescriptor
{
    public string Namespace => "workflow";
    public DescriptorKind Kind => DescriptorKind.Workflow;
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public DescriptorState State { get; init; } = DescriptorState.Active;
    public string? SupersededById { get; init; }
    public string ContractHash { get; init; } = string.Empty;
    public string DefinitionHash { get; init; } = string.Empty;
    public int Version { get; init; }

    public VersionedDescriptorRef<SchemaDescriptor>? VariableSchema { get; init; }
    public IReadOnlyList<WorkflowStep> Steps { get; init; } = Array.Empty<WorkflowStep>();
    public WorkflowVariableScope DefaultVariableScope { get; init; } = WorkflowVariableScope.Workflow;
}
