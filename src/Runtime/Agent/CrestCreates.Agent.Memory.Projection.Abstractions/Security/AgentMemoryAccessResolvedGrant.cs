namespace CrestCreates.Agent.Memory.Projection.Abstractions;

public sealed record AgentMemoryAccessResolvedGrant
{
    public required AgentMemoryAccessSourceGrant Grant { get; init; }
}
