namespace CrestCreates.Agent.Memory.Projection.Abstractions;

public sealed record AgentMemoryAccessPreparedArtifacts
{
    public required AgentMemoryAccessHandleIssueResult? Handles { get; init; }
    public required AgentMemoryAccessGrantIssueResult? Grants { get; init; }
    public AgentMemoryArtifactCompensationToken? CompensationToken { get; init; }
    public required AgentMemoryArtifactBatchReceipt Receipt { get; init; }
}
