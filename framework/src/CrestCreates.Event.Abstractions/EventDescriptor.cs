using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;

namespace CrestCreates.Event.Abstractions;

public sealed class EventDescriptor : IVersionedDescriptor
{
    public DescriptorKind Kind => DescriptorKind.Event;
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public DescriptorState State { get; init; } = DescriptorState.Active;
    public string? SupersededById { get; init; }
    public string ContractHash { get; init; } = string.Empty;
    public string DefinitionHash { get; init; } = string.Empty;
    public int Version { get; init; }

    public VersionedDescriptorRef<SchemaDescriptor> PayloadSchema { get; init; }
    public EventCategory Category { get; init; }
    public EventSemantic Semantic { get; init; }
    public EventImportance Importance { get; init; }
    public SchemaChangeKind ChangeKind { get; init; }
}
