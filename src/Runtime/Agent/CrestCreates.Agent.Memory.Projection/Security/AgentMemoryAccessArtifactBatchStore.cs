using System.Collections.Concurrent;
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
    private readonly ConcurrentDictionary<string, IReadOnlyList<AgentMemoryAccessPreparedArtifact>> _batches = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _batchLocks = new();

    public async ValueTask<IReadOnlyList<AgentMemoryAccessPreparedArtifact>> PrepareAsync(
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
        var batchLock = _batchLocks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));

        await batchLock.WaitAsync(cancellationToken);
        try
        {
            return PrepareInternal(key, plan, batchKey.ArtifactPlanHash.Value);
        }
        finally
        {
            batchLock.Release();
        }
    }

    private IReadOnlyList<AgentMemoryAccessPreparedArtifact> PrepareInternal(
        string key,
        IReadOnlyList<AgentMemoryAccessPreparedArtifact> plan,
        string planHashValue)
    {
        if (_batches.TryGetValue(key, out var existing))
        {
            // Compare by plan hash (excludes random artifact IDs) for retry idempotency
            if (existing.Count > 0 && plan.Count > 0
                && !string.Equals(existing[0].PlanHash.Value, planHashValue, StringComparison.Ordinal))
                throw new InvalidOperationException("Security artifact batch plan conflict: plan hash does not match existing batch.");

            return existing;
        }

        var snapshot = plan.Select(p => p with { }).ToArray();
        _batches[key] = snapshot;
        return snapshot;
    }

    public async ValueTask RevokeCreatedAsync(
        AgentMemoryAccessArtifactBatchKey batchKey,
        IReadOnlyList<AgentMemoryAccessPreparedArtifact> artifacts,
        CancellationToken cancellationToken = default)
    {
        var key = batchKey.ToCanonicalKey();
        var batchLock = _batchLocks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));

        await batchLock.WaitAsync(cancellationToken);
        try
        {
            RevokeCreatedInternal(key, artifacts);
        }
        finally
        {
            batchLock.Release();
        }
    }

    private void RevokeCreatedInternal(
        string key,
        IReadOnlyList<AgentMemoryAccessPreparedArtifact> artifacts)
    {
        // Match by logical identity (ResourceKind+ResourceId) not random ArtifactId,
        // because retries generate new GUIDs that won't match stored entries.
        var createdResourceKeys = artifacts
            .Where(a => a.Disposition == PreparedArtifactDisposition.CreatedByBatch)
            .Select(a => $"{a.ResourceKind}:{a.ResourceId}")
            .ToHashSet(StringComparer.Ordinal);

        if (_batches.TryGetValue(key, out var existing))
        {
            var retained = existing.Where(a =>
                a.Disposition == PreparedArtifactDisposition.ReusedExisting
                || !createdResourceKeys.Contains($"{a.ResourceKind}:{a.ResourceId}")).ToArray();
            _batches[key] = retained;
        }
    }
}