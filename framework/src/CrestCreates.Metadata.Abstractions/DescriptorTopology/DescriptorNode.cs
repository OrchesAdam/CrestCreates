using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Metadata.Abstractions.DescriptorTopology;

public sealed record DescriptorNode
{
    public required DescriptorRef Ref { get; init; }
    public required DescriptorKind Kind { get; init; }
    public required string Name { get; init; }
    public required DescriptorState State { get; init; }
    public string? ContractHash { get; init; }
    public string? SupersededById { get; init; }

    public required IReadOnlySet<int> OutgoingEdgeIndices { get; init; }
    public required IReadOnlySet<int> IncomingEdgeIndices { get; init; }
}
