using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Metadata.ContextPack.Abstractions;

public sealed record ScenarioTraversalStep
{
    public required RelationshipKind FollowKind { get; init; }
    public ScenarioTraversalDirection Direction { get; init; } = ScenarioTraversalDirection.Dependencies;
    public string? Role { get; init; }
    public DescriptorKind? TargetKind { get; init; }
    public int MaxDepth { get; init; } = 1;
}
