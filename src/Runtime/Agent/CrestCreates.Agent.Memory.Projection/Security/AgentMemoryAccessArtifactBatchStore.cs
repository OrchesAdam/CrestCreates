using CrestCreates.Agent.Memory.Projection.Abstractions;
using CrestCreates.Agent.Memory.Tools;

namespace CrestCreates.Agent.Memory.Projection.Security;

/// <summary>
/// In-memory prepared-artifact batch store. Batch plans are immutable;
/// retries with the same plan hash are idempotent (return first-created artifacts).
/// Plan hash comparison replaces full artifact SequenceEqual to support retry
/// idempotency when random artifact IDs differ.
/// </summary>
internal sealed class AgentMemoryAccessArtifactBatchStore : IAgentMemoryAccessArtifactBatchStore
{
    private readonly object _gate = new();
    private readonly Dictionary<string, IReadOnlyList<AgentMemoryAccessPreparedArtifact>> _batches = new(StringComparer.Ordinal);

    public ValueTask<IReadOnlyList<AgentMemoryAccessPreparedArtifact>> PrepareAsync(
        AgentMemoryAccessArtifactBatchKey batchKey,
        IReadOnlyList<AgentMemoryAccessPreparedArtifact> plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(batchKey);
        ArgumentNullException.ThrowIfNull(plan);
        if (string.IsNullOrWhiteSpace(batchKey.ArtifactPlanHash.Value))
            throw new InvalidOperationException("Artifact plan hash is required.");
        if (plan.Count == 0)
            throw new InvalidOperationException("Artifact plan must not be empty.");

        var key = batchKey.ToCanonicalKey();
        var snapshot = plan.Select(p => p with { }).ToArray();

        lock (_gate)
        {
            if (_batches.TryGetValue(key, out var existing))
            {
                // Compare by plan hash (excludes random artifact IDs) for retry idempotency
                if (existing.Count > 0 && snapshot.Length > 0
                    && !string.Equals(existing[0].PlanHash.Value, snapshot[0].PlanHash.Value, StringComparison.Ordinal))
                    throw new InvalidOperationException("Security artifact batch plan conflict: plan hash does not match existing batch.");

                return ValueTask.FromResult(existing);
            }

            _batches[key] = snapshot;
            return ValueTask.FromResult<IReadOnlyList<AgentMemoryAccessPreparedArtifact>>(snapshot);
        }
    }

    public ValueTask RevokeCreatedAsync(
        AgentMemoryAccessArtifactBatchKey batchKey,
        IReadOnlyList<AgentMemoryAccessPreparedArtifact> artifacts,
        CancellationToken cancellationToken = default)
    {
        var key = batchKey.ToCanonicalKey();
        // Match by logical identity (ResourceKind+ResourceId) not random ArtifactId,
        // because retries generate new GUIDs that won't match stored entries.
        var createdResourceKeys = artifacts
            .Where(a => a.Disposition == PreparedArtifactDisposition.CreatedByBatch)
            .Select(a => $"{a.ResourceKind}:{a.ResourceId}")
            .ToHashSet(StringComparer.Ordinal);

        lock (_gate)
        {
            if (_batches.TryGetValue(key, out var existing))
            {
                var retained = existing.Where(a =>
                    a.Disposition == PreparedArtifactDisposition.ReusedExisting
                    || !createdResourceKeys.Contains($"{a.ResourceKind}:{a.ResourceId}")).ToArray();
                _batches[key] = retained;
            }
        }

        return ValueTask.CompletedTask;
    }
}