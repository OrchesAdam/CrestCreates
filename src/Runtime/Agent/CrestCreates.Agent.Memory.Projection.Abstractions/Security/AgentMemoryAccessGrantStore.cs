namespace CrestCreates.Agent.Memory.Projection.Abstractions;

/// <summary>
/// Canonical grant store. Same read-purification semantics as HandleStore.
/// </summary>
public interface IAgentMemoryAccessGrantStore
{
    ValueTask<AgentMemoryAccessGrantIssueResult> TryIssueBatchAsync(
        AgentMemoryAccessArtifactBatchKey batchKey,
        IReadOnlyList<AgentMemoryAccessSourceGrant> grants,
        int maxActivePerResource,
        int maxActivePerOperation,
        CancellationToken cancellationToken = default);

    ValueTask<AgentMemoryAccessSourceGrant?> GetAsync(
        string grantId,
        CancellationToken cancellationToken = default);

    ValueTask RevokeAsync(
        string grantId,
        AgentMemoryCallerKind expectedCallerKind,
        CancellationToken cancellationToken = default);
}
