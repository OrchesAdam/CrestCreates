using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Runtime.Persistence.Abstractions.State;

public sealed record RuntimeStateValue
{
    public required string TypeId { get; init; }

    public DescriptorRef? SchemaRef { get; init; }

    public required string JsonPayload { get; init; }
}
