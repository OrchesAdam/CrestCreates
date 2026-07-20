using System.Collections.Concurrent;

namespace CrestCreates.Agent.Memory.Tools;

/// <summary>
/// Development in-memory prepared-artifact store. Batch plans are immutable;
/// retries with a different plan are conflicts, and rollback never revokes a
/// reused artifact.
/// </summary>
public sealed class AgentMemorySecurityArtifactBatchStore : IAgentMemorySecurityArtifactBatchStore
{
    private readonly ConcurrentDictionary<string, IReadOnlyList<AgentMemoryPreparedSecurityArtifact>> _batches = new(StringComparer.Ordinal);

    public ValueTask<IReadOnlyList<AgentMemoryPreparedSecurityArtifact>> PrepareAsync(
        AgentMemorySecurityArtifactBatchKey batchKey,
        IReadOnlyList<AgentMemoryPreparedSecurityArtifact> plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(batchKey);
        ArgumentNullException.ThrowIfNull(plan);
        if (string.IsNullOrWhiteSpace(batchKey.ArtifactPlanHash))
            throw new InvalidOperationException("Artifact plan hash is required.");
        var key = CanonicalBatchKey(batchKey);
        var snapshot = plan.Select(item => item with { }).ToArray();
        if (_batches.TryGetValue(key, out var existing))
        {
            if (!existing.SequenceEqual(snapshot))
                throw new InvalidOperationException("Security artifact batch plan conflict.");
            return ValueTask.FromResult(existing);
        }
        _batches[key] = snapshot;
        return ValueTask.FromResult<IReadOnlyList<AgentMemoryPreparedSecurityArtifact>>(snapshot);
    }

    public ValueTask RevokeCreatedAsync(
        AgentMemorySecurityArtifactBatchKey batchKey,
        IReadOnlyList<AgentMemoryPreparedSecurityArtifact> artifacts,
        CancellationToken cancellationToken = default)
    {
        var key = CanonicalBatchKey(batchKey);
        if (_batches.TryGetValue(key, out var existing))
        {
            var retained = existing.Where(item =>
                item.Disposition == PreparedArtifactDisposition.ReusedExisting
                || !artifacts.Contains(item)).ToArray();
            _batches[key] = retained;
        }
        return ValueTask.CompletedTask;
    }

    private static string CanonicalBatchKey(AgentMemorySecurityArtifactBatchKey key)
        => string.Join("|", key.OriginKind, key.LogicalInvocationKeyHash,
            key.InvocationFingerprint, key.ArtifactPurpose,
            key.PreparationOrdinal, key.ArtifactPlanHash);
}
