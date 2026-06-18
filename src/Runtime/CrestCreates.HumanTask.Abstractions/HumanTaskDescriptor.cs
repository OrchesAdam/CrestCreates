using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;

namespace CrestCreates.HumanTask.Abstractions;

public sealed class HumanTaskDescriptor : IVersionedDescriptor
{
    public string Namespace => "humantask";
    public DescriptorKind Kind => DescriptorKind.HumanTask;
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public DescriptorState State { get; init; } = DescriptorState.Active;
    public string? SupersededById { get; init; }
    public string ContractHash { get; init; } = string.Empty;
    public string DefinitionHash { get; init; } = string.Empty;
    public int Version { get; init; }

    public VersionedDescriptorRef<IInteractionDescriptor> Interaction { get; init; }
    public VersionedDescriptorRef<SchemaDescriptor>? InputSchema { get; init; }
    public VersionedDescriptorRef<SchemaDescriptor>? OutputSchema { get; init; }
    public AssigneeStrategy AssigneeStrategy { get; init; }
    public TimeSpan? Timeout { get; init; }
    public string? Permissions { get; init; }
    public IReadOnlyList<CompletionOutcome> Outcomes { get; init; } = Array.Empty<CompletionOutcome>();
}
