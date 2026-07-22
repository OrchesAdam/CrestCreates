namespace CrestCreates.Agent.Memory.Projection.Abstractions;

/// <summary>
/// Projection-neutral batch store. Preserves PrepareAsync/RevokeCreatedAsync
/// semantics from existing IAgentMemorySecurityArtifactBatchStore.
/// Uses AgentMemoryAccessPreparedArtifact (not old AgentMemoryPreparedSecurityArtifact).
/// </summary>
public interface IAgentMemoryAccessArtifactBatchStore
{
    ValueTask<IReadOnlyList<AgentMemoryAccessPreparedArtifact>> PrepareAsync(
        AgentMemoryAccessArtifactBatchKey batchKey,
        IReadOnlyList<AgentMemoryAccessPreparedArtifact> plan,
        CancellationToken cancellationToken = default);

    ValueTask RevokeCreatedAsync(
        AgentMemoryAccessArtifactBatchKey batchKey,
        IReadOnlyList<AgentMemoryAccessPreparedArtifact> artifacts,
        CancellationToken cancellationToken = default);
}
