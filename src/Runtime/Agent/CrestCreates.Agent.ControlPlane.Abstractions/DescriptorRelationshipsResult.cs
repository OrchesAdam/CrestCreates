using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorRelationship;

namespace CrestCreates.Agent.ControlPlane.Abstractions;

public sealed record DescriptorRelationshipsResult
{
    public required DescriptorRef Subject { get; init; }
    public required IReadOnlyList<DescriptorRelationship> Dependencies { get; init; }
    public required IReadOnlyList<DescriptorRelationship> Dependents { get; init; }
}
