namespace CrestCreates.Agent.Memory.Tools;

/// <summary>
/// Development in-memory prepared-artifact store. Batch plans are immutable;
/// retries with a different plan are conflicts, and rollback never revokes a
/// reused artifact.
/// </summary>
public sealed class AgentMemorySecurityArtifactBatchStore : IAgentMemorySecurityArtifactBatchStore
{
    private readonly object _gate = new();
    private readonly Dictionary<string, IReadOnlyList<AgentMemoryPreparedSecurityArtifact>> _batches = new(StringComparer.Ordinal);

    public ValueTask<IReadOnlyList<AgentMemoryPreparedSecurityArtifact>> PrepareAsync(
        AgentMemorySecurityArtifactBatchKey batchKey,
        IReadOnlyList<AgentMemoryPreparedSecurityArtifact> plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(batchKey);
        ArgumentNullException.ThrowIfNull(plan);
        if (string.IsNullOrWhiteSpace(batchKey.ArtifactPlanHash.Value))
            throw new InvalidOperationException("Artifact plan hash is required.");
        var key = batchKey.ToCanonicalKey();
        var snapshot = plan.Select(item => item with { }).ToArray();
        lock (_gate)
        {
            if (_batches.TryGetValue(key, out var existing))
            {
                if (!existing.SequenceEqual(snapshot))
                    throw new InvalidOperationException("Security artifact batch plan conflict.");
                return ValueTask.FromResult(existing);
            }
            _batches[key] = snapshot;
            return ValueTask.FromResult<IReadOnlyList<AgentMemoryPreparedSecurityArtifact>>(snapshot);
        }
    }

    public ValueTask RevokeCreatedAsync(
        AgentMemorySecurityArtifactBatchKey batchKey,
        IReadOnlyList<AgentMemoryPreparedSecurityArtifact> artifacts,
        CancellationToken cancellationToken = default)
    {
        var key = batchKey.ToCanonicalKey();
        var artifactIds = artifacts
            .Where(item => item.Disposition == PreparedArtifactDisposition.CreatedByBatch)
            .Select(item => item.ArtifactId)
            .ToHashSet(StringComparer.Ordinal);
        lock (_gate)
        {
            if (_batches.TryGetValue(key, out var existing))
            {
                var retained = existing.Where(item =>
                    item.Disposition == PreparedArtifactDisposition.ReusedExisting
                    || !artifactIds.Contains(item.ArtifactId)).ToArray();
                _batches[key] = retained;
            }
        }
        return ValueTask.CompletedTask;
    }
}
