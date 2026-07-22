namespace CrestCreates.Agent.Memory.Projection.Abstractions;

public sealed record AgentMemoryAccessGrantIssueResult
{
    public required IReadOnlyList<AgentMemoryAccessSourceGrant> Grants { get; init; }
    public bool ReusedExisting { get; init; }
}
