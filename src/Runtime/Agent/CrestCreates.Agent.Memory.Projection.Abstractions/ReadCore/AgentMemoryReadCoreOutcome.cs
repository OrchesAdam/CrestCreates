namespace CrestCreates.Agent.Memory.Projection.Abstractions;

/// <summary>
/// Unified ReadCore outcome. CompensationToken present only when this execution
/// created security artifacts. Never serialized into protocol output.
/// </summary>
public sealed record AgentMemoryReadCoreOutcome<T>
{
    public required T Result { get; init; }
    public required string ScopeFingerprint { get; init; }
    public required int MaximumAuditFacts { get; init; }
    public required AgentMemoryArtifactBatchReceipt Receipt { get; init; }
    public AgentMemoryArtifactCompensationToken? CompensationToken { get; init; }
}
