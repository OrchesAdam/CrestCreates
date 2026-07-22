namespace CrestCreates.Agent.Memory.Projection.Abstractions;

/// <summary>
/// Projection-neutral artifact coordinator. Single preparation boundary for
/// plan hashing, dual-origin binding, and handle/grant issuance.
/// </summary>
public interface IAgentMemoryAccessArtifactCoordinator
{
    ValueTask<AgentMemoryAccessPreparedArtifacts> PrepareAsync(
        AgentMemoryAccessPrincipal principal,
        AgentMemoryArtifactOrigin origin,
        AgentMemoryAccessScope scope,
        string artifactPurpose,
        int preparationOrdinal,
        IReadOnlyList<AgentMemoryAccessResourceHandle> handles,
        IReadOnlyList<AgentMemoryAccessSourceGrant> grants,
        CancellationToken cancellationToken = default);

    ValueTask RevokeCreatedAsync(
        AgentMemoryArtifactCompensationToken token,
        CancellationToken cancellationToken = default);
}
