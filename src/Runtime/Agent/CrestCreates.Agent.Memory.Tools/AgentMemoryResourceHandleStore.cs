using CrestCreates.Agent.Memory.Tools;

namespace CrestCreates.Agent.Memory.Tools;

/// <summary>
/// Development in-memory handle store. Entries are lazily marked expired and
/// are intentionally not evicted; durable providers must implement bounded
/// retention and expiry cleanup.
/// </summary>
public sealed class AgentMemoryResourceHandleStore : IAgentMemoryResourceHandleStore
{
    private readonly object _gate = new();
    private readonly Dictionary<string, AgentMemoryResourceHandle> _handles = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IReadOnlyList<string>> _batches = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _batchPlans = new(StringComparer.Ordinal);

    public ValueTask<AgentMemoryResourceHandleIssueResult> TryIssueBatchAsync(
        AgentMemorySecurityArtifactBatchKey batchKey,
        IReadOnlyList<AgentMemoryResourceHandle> handles,
        int maxActiveHandlesPerResource,
        CancellationToken cancellationToken = default)
        => TryIssueBatchAsync(batchKey, handles, maxActiveHandlesPerResource, int.MaxValue, cancellationToken);

    public ValueTask<AgentMemoryResourceHandleIssueResult> TryIssueBatchAsync(
        AgentMemorySecurityArtifactBatchKey batchKey,
        IReadOnlyList<AgentMemoryResourceHandle> handles,
        int maxActiveHandlesPerResource,
        int maxActiveHandlesPerInvocation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(batchKey);
        ArgumentNullException.ThrowIfNull(handles);
        if (handles.Count == 0 || handles.Any(item => string.IsNullOrWhiteSpace(item.HandleId)))
            throw new InvalidOperationException("A non-empty opaque handle batch is required.");
        if (maxActiveHandlesPerResource <= 0 || maxActiveHandlesPerInvocation < handles.Count)
            throw new InvalidOperationException("Resource handle quota is exhausted.");
        var key = batchKey.ToCanonicalKey();
        var identity = batchKey.ToIdentityKey();
        lock (_gate)
        {
            if (_batchPlans.TryGetValue(identity, out var existingPlan)
                && !string.Equals(existingPlan, batchKey.ArtifactPlanHash.Value, StringComparison.Ordinal))
                throw new InvalidOperationException("Security artifact batch plan conflicts with an existing preparation.");
            if (_batches.TryGetValue(key, out var existingIds))
                return ValueTask.FromResult(new AgentMemoryResourceHandleIssueResult
                {
                    Handles = existingIds.Select(id => _handles[id]).ToArray(),
                    ReusedExisting = true
                });

            if (handles.Select(item => item.HandleId).Distinct(StringComparer.Ordinal).Count() != handles.Count)
                throw new InvalidOperationException("Handle ids must be unique within a batch.");
            var firstPrincipal = handles[0].Principal;
            if (handles.Any(item => item.Principal != firstPrincipal))
                throw new InvalidOperationException("A handle batch must have one trusted principal.");
            var now = DateTimeOffset.UtcNow;
            var requestedByResource = handles.GroupBy(item => (ResourceKind: item.ResourceKind, ResourceId: item.ResourceId, ScopeFingerprint: item.ScopeFingerprint));
            foreach (var group in requestedByResource)
            {
                if (group.Any(handle => _handles.ContainsKey(handle.HandleId)))
                    throw new InvalidOperationException("Resource handle identity collision.");
                var active = _handles.Values.Count(item => item.ResourceKind == group.Key.ResourceKind
                    && item.ResourceId == group.Key.ResourceId
                    && item.Principal == firstPrincipal
                    && item.ScopeFingerprint == group.Key.ScopeFingerprint
                    && item.State == AgentMemorySecurityArtifactState.Active
                    && item.ExpiresAt > now);
                if (active + group.Count() > maxActiveHandlesPerResource)
                    throw new InvalidOperationException("Active resource handle quota is exhausted.");
            }
            var invocationActive = _handles.Values.Count(item => item.Principal == firstPrincipal
                && item.State == AgentMemorySecurityArtifactState.Active
                && item.ExpiresAt > now);
            if (invocationActive + handles.Count > maxActiveHandlesPerInvocation)
                throw new InvalidOperationException("Invocation resource handle quota is exhausted.");
            foreach (var handle in handles)
                _handles[handle.HandleId] = handle;
            _batches[key] = handles.Select(item => item.HandleId).ToArray();
            _batchPlans[identity] = batchKey.ArtifactPlanHash.Value;
            return ValueTask.FromResult(new AgentMemoryResourceHandleIssueResult
            {
                Handles = handles.ToArray(),
                ReusedExisting = false
            });
        }
    }

    public ValueTask<AgentMemoryResourceHandle?> GetAsync(string handleId, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (!_handles.TryGetValue(handleId, out var handle))
                return ValueTask.FromResult<AgentMemoryResourceHandle?>(null);
            if (handle.State == AgentMemorySecurityArtifactState.Active && handle.ExpiresAt <= DateTimeOffset.UtcNow)
            {
                handle = handle with { State = AgentMemorySecurityArtifactState.Expired };
                _handles[handleId] = handle;
            }
            return ValueTask.FromResult<AgentMemoryResourceHandle?>(handle);
        }
    }

    public ValueTask RevokeAsync(string handleId, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_handles.TryGetValue(handleId, out var handle))
                _handles[handleId] = handle with { State = AgentMemorySecurityArtifactState.Revoked };
        }
        return ValueTask.CompletedTask;
    }
}
