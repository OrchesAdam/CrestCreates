namespace CrestCreates.Agent.Memory.Projection.Abstractions;

/// <summary>
/// Canonical handle store. GetAsync returns read-only state view —
/// if artifact is Active but ExpiresAt &lt;= now, returns artifact with State = Expired
/// WITHOUT persisting the transition. Cleanup via independent retention mechanism.
/// </summary>
public interface IAgentMemoryAccessHandleStore
{
    ValueTask<AgentMemoryAccessHandleIssueResult> TryIssueBatchAsync(
        AgentMemoryAccessArtifactBatchKey batchKey,
        IReadOnlyList<AgentMemoryAccessResourceHandle> handles,
        int maxActivePerResource,
        int maxActivePerOperation,
        CancellationToken cancellationToken = default);

    ValueTask<AgentMemoryAccessResourceHandle?> GetAsync(
        string handleId,
        CancellationToken cancellationToken = default);

    ValueTask RevokeAsync(
        string handleId,
        AgentMemoryCallerKind expectedCallerKind,
        CancellationToken cancellationToken = default);
}
