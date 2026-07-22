namespace CrestCreates.Agent.Memory.Projection.Abstractions;

public sealed record AgentMemoryAccessHandleIssueResult
{
    public required IReadOnlyList<AgentMemoryAccessResourceHandle> Handles { get; init; }
    public bool ReusedExisting { get; init; }
}
